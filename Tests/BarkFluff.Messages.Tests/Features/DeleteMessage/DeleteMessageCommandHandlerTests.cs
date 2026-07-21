using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Messages.Features.DeleteMessage;
using BarkFluff.Messages.Infrastructure;
using BarkFluff.Messages.Persistence.Services;
using BarkFluff.Proto.Messages;
using BarkFluff.Shared.Exceptions.Messages;

namespace BarkFluff.Messages.Tests.Features.DeleteMessage;

public class DeleteMessageCommandHandlerTests
{
    private readonly TestHelper _h = new();
    private readonly MessageQueueSender _queueSender;

    public DeleteMessageCommandHandlerTests()
    {
        _queueSender = new MessageQueueSender(_h.PublishEndpointMock.Object);
    }

    private DeleteMessageCommandHandler CreateHandler(long userId)
    {
        return new DeleteMessageCommandHandler(
            _h.MessagesStorage,
            _h.ChatsStorage,
            _h.PinnedMessagesStorage,
            _h.CreateUserContext(userId),
            _queueSender,
            _h.Metrics,
            TestHelper.CreateLogger<DeleteMessageCommandHandler>());
    }

    [Fact]
    public async Task Handle_ValidDelete_SoftDeletesMessage()
    {
        var userId = 1L;
        var chat = await _h.SeedChat(memberUserIds: [userId, 2]);
        var message = await _h.SeedMessage(chat.Id, userId, "delete me");
        var handler = CreateHandler(userId);

        var result = await handler.Handle(new DeleteMessageCommand { MessageId = message.Id }, CancellationToken.None);

        result.Should().NotBeNull();
        var dbMessage = await _h.MessagesStorage.GetMessageById(message.Id);
        dbMessage!.IsDeleted.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_MessageNotFound_ThrowsMessageNotFoundException()
    {
        var handler = CreateHandler(1);

        var act = async () => await handler.Handle(new DeleteMessageCommand { MessageId = 999999 }, CancellationToken.None);

        await act.Should().ThrowAsync<MessageNotFoundException>();
    }

    [Fact]
    public async Task Handle_NotOwner_ThrowsNoPermissionException()
    {
        var chat = await _h.SeedChat(memberUserIds: [1, 2]);
        var message = await _h.SeedMessage(chat.Id, 1, "test");
        var handler = CreateHandler(2);

        var act = async () => await handler.Handle(new DeleteMessageCommand { MessageId = message.Id }, CancellationToken.None);

        await act.Should().ThrowAsync<NoPermissionException>();
    }

    [Fact]
    public async Task Handle_SystemMessage_ThrowsNoPermissionException()
    {
        var userId = 1L;
        var chat = await _h.SeedChat(memberUserIds: [userId, 2]);
        var message = await _h.SeedMessage(chat.Id, userId, "system", Domain.MessageContentType.System);
        var handler = CreateHandler(userId);

        var act = async () => await handler.Handle(new DeleteMessageCommand { MessageId = message.Id }, CancellationToken.None);

        await act.Should().ThrowAsync<NoPermissionException>();
    }

    [Fact]
    public async Task Handle_AlreadyDeleted_ReturnsIdempotentResponse()
    {
        var userId = 1L;
        var chat = await _h.SeedChat(memberUserIds: [userId, 2]);
        var message = await _h.SeedMessage(chat.Id, userId, "deleted", isDeleted: true);
        var handler = CreateHandler(userId);

        var result = await handler.Handle(new DeleteMessageCommand { MessageId = message.Id }, CancellationToken.None);

        result.Should().NotBeNull();
        _h.PublishEndpointMock.Verify(p => p.Publish(It.IsAny<Shared.Queue.Messages.MessageDeletedEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_PublishesDeletedEvent()
    {
        var userId = 1L;
        var chat = await _h.SeedChat(memberUserIds: [userId, 2]);
        var message = await _h.SeedMessage(chat.Id, userId, "test");
        var handler = CreateHandler(userId);

        await handler.Handle(new DeleteMessageCommand { MessageId = message.Id }, CancellationToken.None);

        _h.PublishEndpointMock.Verify(p => p.Publish(It.IsAny<Shared.Queue.Messages.MessageDeletedEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_PinnedMessage_DeletesPinAndPublishesUnpinnedEvent()
    {
        var userId = 1L;
        var chat = await _h.SeedChat(memberUserIds: [userId, 2]);
        var message = await _h.SeedMessage(chat.Id, userId, "pinned msg");
        await _h.SeedPinnedMessage(chat.Id, message.Id, userId);
        var handler = CreateHandler(userId);

        await handler.Handle(new DeleteMessageCommand { MessageId = message.Id }, CancellationToken.None);

        _h.PublishEndpointMock.Verify(p => p.Publish(It.IsAny<Shared.Queue.Messages.MessageUnpinnedEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_NotPinnedMessage_DoesNotPublishUnpinnedEvent()
    {
        var userId = 1L;
        var chat = await _h.SeedChat(memberUserIds: [userId, 2]);
        var message = await _h.SeedMessage(chat.Id, userId, "regular");
        var handler = CreateHandler(userId);

        await handler.Handle(new DeleteMessageCommand { MessageId = message.Id }, CancellationToken.None);

        _h.PublishEndpointMock.Verify(p => p.Publish(It.IsAny<Shared.Queue.Messages.MessageUnpinnedEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_FederatedChat_PublishesDeletedEventWithFederatedFields()
    {
        var userId = 1L;
        var localUuid = Guid.NewGuid();
        var remoteUuid = Guid.NewGuid();
        var chat = await _h.SeedFederatedChat(userId, localUuid, remoteUuid, "remote.test");
        var federatedId = Guid.NewGuid();
        var message = await _h.SeedFederatedMessage(chat.Id, federatedId, senderUuid: localUuid, senderId: userId, text: "bye");
        var handler = CreateHandler(userId);

        await handler.Handle(new DeleteMessageCommand { MessageId = message.Id }, CancellationToken.None);

        _h.PublishEndpointMock.Verify(p => p.Publish(
            It.Is<Shared.Queue.Messages.MessageDeletedEvent>(e =>
                e.IsFederated
                && e.FederatedId == federatedId
                && e.RemoteParticipants.Count == 1
                && e.RemoteParticipants[0].Uuid == remoteUuid),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
