using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Messages.Features.PinMessage;
using BarkFluff.Messages.Infrastructure;
using BarkFluff.Messages.Persistence.Services;
using BarkFluff.Proto.Files;
using BarkFluff.Proto.Users;
using BarkFluff.Shared.Exceptions.Messages;

using Grpc.Core;

namespace BarkFluff.Messages.Tests.Features.PinMessage;

public class PinMessageCommandHandlerTests
{
    private readonly TestHelper _h = new();
    private readonly Mock<FilesServerApi.FilesServerApiClient> _filesClient;
    private readonly Mock<UsersServerApi.UsersServerApiClient> _usersClient;
    private readonly MessageQueueSender _queueSender;

    public PinMessageCommandHandlerTests()
    {
        _filesClient = new Mock<FilesServerApi.FilesServerApiClient>();
        _usersClient = new Mock<UsersServerApi.UsersServerApiClient>();
        _queueSender = new MessageQueueSender(_h.PublishEndpointMock.Object);
        SetupUsersClient();
    }

    private PinMessageCommandHandler CreateHandler(long userId)
    {
        return new PinMessageCommandHandler(
            _h.PinnedMessagesStorage,
            _h.MessagesStorage,
            _h.ChatsStorage,
            _filesClient.Object,
            _usersClient.Object,
            _h.CreateUserContext(userId),
            _queueSender,
            _h.Metrics,
            TestHelper.CreateLogger<PinMessageCommandHandler>());
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
    public async Task Handle_ValidPin_PinsMessage()
    {
        var userId = 1L;
        var chat = await _h.SeedChat(memberUserIds: [userId, 2]);
        var message = await _h.SeedMessage(chat.Id, userId, "pin me");
        var handler = CreateHandler(userId);

        var result = await handler.Handle(new PinMessageCommand { ChatId = chat.Id, MessageId = message.Id }, CancellationToken.None);

        result.Should().NotBeNull();
        result.Pinned.Should().NotBeNull();
    }

    [Fact]
    public async Task Handle_NoAccess_ThrowsNoAccessToChatException()
    {
        var chat = await _h.SeedChat(memberUserIds: [99, 100]);
        var message = await _h.SeedMessage(chat.Id, 99, "test");
        var handler = CreateHandler(1);

        var act = async () => await handler.Handle(new PinMessageCommand { ChatId = chat.Id, MessageId = message.Id }, CancellationToken.None);

        await act.Should().ThrowAsync<NoAccessToChatException>();
    }

    [Fact]
    public async Task Handle_MessageNotFound_ThrowsMessageNotFoundException()
    {
        var userId = 1L;
        var chat = await _h.SeedChat(memberUserIds: [userId, 2]);
        var handler = CreateHandler(userId);

        var act = async () => await handler.Handle(new PinMessageCommand { ChatId = chat.Id, MessageId = 99999 }, CancellationToken.None);

        await act.Should().ThrowAsync<MessageNotFoundException>();
    }

    [Fact]
    public async Task Handle_DeletedMessage_ThrowsMessageNotFoundException()
    {
        var userId = 1L;
        var chat = await _h.SeedChat(memberUserIds: [userId, 2]);
        var message = await _h.SeedMessage(chat.Id, userId, "deleted", isDeleted: true);
        var handler = CreateHandler(userId);

        var act = async () => await handler.Handle(new PinMessageCommand { ChatId = chat.Id, MessageId = message.Id }, CancellationToken.None);

        await act.Should().ThrowAsync<MessageNotFoundException>();
    }

    [Fact]
    public async Task Handle_AlreadyPinned_ReturnsIdempotentResponse()
    {
        var userId = 1L;
        var chat = await _h.SeedChat(memberUserIds: [userId, 2]);
        var message = await _h.SeedMessage(chat.Id, userId, "already pinned");
        await _h.SeedPinnedMessage(chat.Id, message.Id, userId);
        var handler = CreateHandler(userId);

        var result = await handler.Handle(new PinMessageCommand { ChatId = chat.Id, MessageId = message.Id }, CancellationToken.None);

        result.Should().NotBeNull();
        _h.PublishEndpointMock.Verify(p => p.Publish(It.IsAny<Shared.Queue.Messages.MessagePinnedEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_CreatesSystemMessageAndPublishesEvents()
    {
        var userId = 1L;
        var chat = await _h.SeedChat(memberUserIds: [userId, 2]);
        var message = await _h.SeedMessage(chat.Id, userId, "pin me");
        var handler = CreateHandler(userId);

        await handler.Handle(new PinMessageCommand { ChatId = chat.Id, MessageId = message.Id }, CancellationToken.None);

        _h.PublishEndpointMock.Verify(p => p.Publish(It.IsAny<Shared.Queue.Messages.NewMessageEvent>(), It.IsAny<CancellationToken>()), Times.Once);
        _h.PublishEndpointMock.Verify(p => p.Publish(It.IsAny<Shared.Queue.Messages.MessagePinnedEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
