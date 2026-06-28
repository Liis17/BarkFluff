using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Messages.Features.UnpinMessage;
using BarkFluff.Messages.Infrastructure;
using BarkFluff.Messages.Persistence.Services;
using BarkFluff.Proto.Messages;
using BarkFluff.Proto.Users;
using BarkFluff.Shared.Exceptions.Messages;

using Grpc.Core;

namespace BarkFluff.Messages.Tests.Features.UnpinMessage;

public class UnpinMessageCommandHandlerTests
{
    private readonly TestHelper _h = new();
    private readonly Mock<UsersServerApi.UsersServerApiClient> _usersClient;
    private readonly MessageQueueSender _queueSender;

    public UnpinMessageCommandHandlerTests()
    {
        _usersClient = new Mock<UsersServerApi.UsersServerApiClient>();
        _queueSender = new MessageQueueSender(_h.PublishEndpointMock.Object);
        SetupUsersClient();
    }

    private UnpinMessageCommandHandler CreateHandler(long userId)
    {
        return new UnpinMessageCommandHandler(
            _h.PinnedMessagesStorage,
            _h.MessagesStorage,
            _h.ChatsStorage,
            _usersClient.Object,
            _h.CreateUserContext(userId),
            _queueSender,
            _h.Metrics,
            TestHelper.CreateLogger<UnpinMessageCommandHandler>());
    }

    private void SetupUsersClient()
    {
        _usersClient.Setup(c => c.GetByIdAsync(It.IsAny<GetByIdRequest>(), null, null, It.IsAny<CancellationToken>()))
            .Returns(new AsyncUnaryCall<GetByIdResponse>(
                Task.FromResult(new GetByIdResponse {                 User = new User { Id = 1, FirstName = "Test", LastName = "User" } }),
                Task.FromResult(new Metadata()),
                () => Status.DefaultSuccess,
                () => new Metadata(),
                () => { }));
    }

    [Fact]
    public async Task Handle_ValidUnpin_RemovesPin()
    {
        var userId = 1L;
        var chat = await _h.SeedChat(memberUserIds: [userId, 2]);
        var message = await _h.SeedMessage(chat.Id, userId, "pinned");
        await _h.SeedPinnedMessage(chat.Id, message.Id, userId);
        var handler = CreateHandler(userId);

        var result = await handler.Handle(new UnpinMessageCommand { ChatId = chat.Id, MessageId = message.Id }, CancellationToken.None);

        result.Should().NotBeNull();
        var pin = await _h.PinnedMessagesStorage.GetPinByMessageIdAsync(chat.Id, message.Id);
        pin.Should().BeNull();
    }

    [Fact]
    public async Task Handle_NoAccess_ThrowsNoAccessToChatException()
    {
        var chat = await _h.SeedChat(memberUserIds: [99, 100]);
        var handler = CreateHandler(1);

        var act = async () => await handler.Handle(new UnpinMessageCommand { ChatId = chat.Id, MessageId = 1 }, CancellationToken.None);

        await act.Should().ThrowAsync<NoAccessToChatException>();
    }

    [Fact]
    public async Task Handle_NotPinned_ReturnsIdempotentResponse()
    {
        var userId = 1L;
        var chat = await _h.SeedChat(memberUserIds: [userId, 2]);
        var handler = CreateHandler(userId);

        var result = await handler.Handle(new UnpinMessageCommand { ChatId = chat.Id, MessageId = 99999 }, CancellationToken.None);

        result.Should().NotBeNull();
        _h.PublishEndpointMock.Verify(p => p.Publish(It.IsAny<Shared.Queue.Messages.MessageUnpinnedEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_PublishesSystemMessageAndUnpinnedEvent()
    {
        var userId = 1L;
        var chat = await _h.SeedChat(memberUserIds: [userId, 2]);
        var message = await _h.SeedMessage(chat.Id, userId, "pinned");
        await _h.SeedPinnedMessage(chat.Id, message.Id, userId);
        var handler = CreateHandler(userId);

        await handler.Handle(new UnpinMessageCommand { ChatId = chat.Id, MessageId = message.Id }, CancellationToken.None);

        _h.PublishEndpointMock.Verify(p => p.Publish(It.IsAny<Shared.Queue.Messages.NewMessageEvent>(), It.IsAny<CancellationToken>()), Times.Once);
        _h.PublishEndpointMock.Verify(p => p.Publish(It.IsAny<Shared.Queue.Messages.MessageUnpinnedEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
