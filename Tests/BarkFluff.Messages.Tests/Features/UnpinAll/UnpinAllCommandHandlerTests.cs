using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Messages.Features.UnpinAll;
using BarkFluff.Messages.Infrastructure;
using BarkFluff.Messages.Persistence.Services;
using BarkFluff.Proto.Messages;
using BarkFluff.Proto.Users;
using BarkFluff.Shared.Exceptions.Messages;

using Grpc.Core;

namespace BarkFluff.Messages.Tests.Features.UnpinAll;

public class UnpinAllCommandHandlerTests
{
    private readonly TestHelper _h = new();
    private readonly Mock<UsersServerApi.UsersServerApiClient> _usersClient;
    private readonly MessageQueueSender _queueSender;

    public UnpinAllCommandHandlerTests()
    {
        _usersClient = new Mock<UsersServerApi.UsersServerApiClient>();
        _queueSender = new MessageQueueSender(_h.PublishEndpointMock.Object);
        SetupUsersClient();
    }

    private UnpinAllCommandHandler CreateHandler(long userId)
    {
        return new UnpinAllCommandHandler(
            _h.PinnedMessagesStorage,
            _h.MessagesStorage,
            _h.ChatsStorage,
            _usersClient.Object,
            _h.CreateUserContext(userId),
            _queueSender,
            _h.Metrics,
            TestHelper.CreateLogger<UnpinAllCommandHandler>());
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
    public async Task Handle_ValidUnpinAll_RemovesAllPins()
    {
        var userId = 1L;
        var chat = await _h.SeedChat(memberUserIds: [userId, 2]);
        var msg1 = await _h.SeedMessage(chat.Id, userId, "msg1");
        var msg2 = await _h.SeedMessage(chat.Id, userId, "msg2");
        await _h.SeedPinnedMessage(chat.Id, msg1.Id, userId);
        await _h.SeedPinnedMessage(chat.Id, msg2.Id, userId);
        var handler = CreateHandler(userId);

        var result = await handler.Handle(new UnpinAllCommand { ChatId = chat.Id }, CancellationToken.None);

        result.UnpinnedCount.Should().Be(2);
        var count = await _h.PinnedMessagesStorage.CountByChatAsync(chat.Id);
        count.Should().Be(0);
    }

    [Fact]
    public async Task Handle_NoAccess_ThrowsNoAccessToChatException()
    {
        var chat = await _h.SeedChat(memberUserIds: [99, 100]);
        var handler = CreateHandler(1);

        var act = async () => await handler.Handle(new UnpinAllCommand { ChatId = chat.Id }, CancellationToken.None);

        await act.Should().ThrowAsync<NoAccessToChatException>();
    }

    [Fact]
    public async Task Handle_NoPinnedMessages_ReturnsIdempotentResponse()
    {
        var userId = 1L;
        var chat = await _h.SeedChat(memberUserIds: [userId, 2]);
        var handler = CreateHandler(userId);

        var result = await handler.Handle(new UnpinAllCommand { ChatId = chat.Id }, CancellationToken.None);

        result.UnpinnedCount.Should().Be(0);
        _h.PublishEndpointMock.Verify(p => p.Publish(It.IsAny<Shared.Queue.Messages.AllMessagesUnpinnedEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_PublishesSystemMessageAndAllUnpinnedEvent()
    {
        var userId = 1L;
        var chat = await _h.SeedChat(memberUserIds: [userId, 2]);
        var msg = await _h.SeedMessage(chat.Id, userId, "msg");
        await _h.SeedPinnedMessage(chat.Id, msg.Id, userId);
        var handler = CreateHandler(userId);

        await handler.Handle(new UnpinAllCommand { ChatId = chat.Id }, CancellationToken.None);

        _h.PublishEndpointMock.Verify(p => p.Publish(It.IsAny<Shared.Queue.Messages.NewMessageEvent>(), It.IsAny<CancellationToken>()), Times.Once);
        _h.PublishEndpointMock.Verify(p => p.Publish(It.IsAny<Shared.Queue.Messages.AllMessagesUnpinnedEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
