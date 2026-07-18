using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Messages.Features.MarkAsRead;
using BarkFluff.Messages.Infrastructure;
using BarkFluff.Messages.Persistence.Services;
using BarkFluff.Shared.Exceptions.Messages;

namespace BarkFluff.Messages.Tests.Features.MarkAsRead;

public class MarkAsReadCommandHandlerTests
{
    private readonly TestHelper _h = new();
    private readonly ReadByQueueSender _queueSender;

    public MarkAsReadCommandHandlerTests()
    {
        _queueSender = new ReadByQueueSender(_h.PublishEndpointMock.Object);
    }

    private MarkAsReadCommandHandler CreateHandler(long userId)
    {
        return new MarkAsReadCommandHandler(
            _h.MessagesStorage,
            _h.ChatsStorage,
            _h.CreateUserContext(userId),
            _queueSender,
            _h.Metrics,
            TestHelper.CreateLogger<MarkAsReadCommandHandler>());
    }

    [Fact]
    public async Task Handle_EmptyMessageIds_ReturnsWithoutError()
    {
        var handler = CreateHandler(1);

        var act = async () => await handler.Handle(new MarkAsReadCommand { MessageIds = [] }, CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Handle_NoAccessToChat_ThrowsNoAccessToChatException()
    {
        var chat = await _h.SeedChat(memberUserIds: [99, 100]);
        var message = await _h.SeedMessage(chat.Id, 99, "test");
        var handler = CreateHandler(1);

        var act = async () => await handler.Handle(new MarkAsReadCommand { MessageIds = [message.Id] }, CancellationToken.None);

        await act.Should().ThrowAsync<NoAccessToChatException>();
    }

    [Fact]
    public async Task Handle_ValidMessages_ReturnsSuccessfully()
    {
        var userId = 1L;
        var chat = await _h.SeedChat(memberUserIds: [userId, 2]);
        var msg1 = await _h.SeedMessage(chat.Id, 2, "msg1", readBy: [2]);
        var msg2 = await _h.SeedMessage(chat.Id, 2, "msg2", readBy: [2]);
        var handler = CreateHandler(userId);

        await handler.Handle(new MarkAsReadCommand { MessageIds = [msg1.Id, msg2.Id] }, CancellationToken.None);

        _h.PublishEndpointMock.Verify(
            p => p.Publish(It.IsAny<Shared.Queue.Messages.MessageReadEvent>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task Handle_AlreadyReadMessage_DoesNotPublishReadEvent()
    {
        var userId = 1L;
        var chat = await _h.SeedChat(memberUserIds: [userId, 2]);
        var message = await _h.SeedMessage(chat.Id, 2, "message", readBy: [2, userId]);
        var handler = CreateHandler(userId);

        await handler.Handle(new MarkAsReadCommand { MessageIds = [message.Id] }, CancellationToken.None);

        _h.PublishEndpointMock.Verify(
            publisher => publisher.Publish(
                It.IsAny<Shared.Queue.Messages.MessageReadEvent>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_NewRead_PublishesOnlyNewReader()
    {
        var userId = 1L;
        var chat = await _h.SeedChat(memberUserIds: [userId, 2]);
        var message = await _h.SeedMessage(chat.Id, 2, "message", readBy: [2]);
        var handler = CreateHandler(userId);

        await handler.Handle(new MarkAsReadCommand { MessageIds = [message.Id] }, CancellationToken.None);

        _h.PublishEndpointMock.Verify(
            publisher => publisher.Publish(
                It.Is<Shared.Queue.Messages.MessageReadEvent>(@event =>
                    @event.NewReadBy.SequenceEqual(new[] { userId })),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_NoMessagesFound_ReturnsWithoutError()
    {
        var handler = CreateHandler(1);

        var act = async () => await handler.Handle(new MarkAsReadCommand { MessageIds = [99999] }, CancellationToken.None);

        await act.Should().NotThrowAsync();
    }
}
