using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Messages.Features.EditMessage;
using BarkFluff.Messages.Infrastructure;
using BarkFluff.Messages.Persistence.Services;
using BarkFluff.Proto.Files;
using BarkFluff.Proto.Messages;
using BarkFluff.Shared.Exceptions.Messages;

using Grpc.Core;

namespace BarkFluff.Messages.Tests.Features.EditMessage;

public class EditMessageCommandHandlerTests
{
    private readonly TestHelper _h = new();
    private readonly Mock<FilesServerApi.FilesServerApiClient> _filesClient;
    private readonly MessageQueueSender _queueSender;

    public EditMessageCommandHandlerTests()
    {
        _filesClient = new Mock<FilesServerApi.FilesServerApiClient>();
        _queueSender = new MessageQueueSender(_h.PublishEndpointMock.Object);
    }

    private EditMessageCommandHandler CreateHandler(long userId)
    {
        return new EditMessageCommandHandler(
            _h.MessagesStorage,
            _h.ChatsStorage,
            _filesClient.Object,
            _h.CreateUserContext(userId),
            _queueSender,
            TestHelper.CreateConfiguration(),
            _h.Metrics,
            TestHelper.CreateLogger<EditMessageCommandHandler>());
    }

    [Fact]
    public async Task Handle_ValidTextEdit_EditsMessage()
    {
        var userId = 1L;
        var chat = await _h.SeedChat(memberUserIds: [userId, 2]);
        var message = await _h.SeedMessage(chat.Id, userId, "original");
        var handler = CreateHandler(userId);

        var result = await handler.Handle(new EditMessageCommand
        {
            MessageId = message.Id,
            Text = "edited"
        }, CancellationToken.None);

        result.Should().NotBeNull();
        result.Message.Content.Text.Should().Be("edited");
    }

    [Fact]
    public async Task Handle_MessageNotFound_ThrowsMessageNotFoundException()
    {
        var handler = CreateHandler(1);

        var act = async () => await handler.Handle(new EditMessageCommand
        {
            MessageId = 999999,
            Text = "test"
        }, CancellationToken.None);

        await act.Should().ThrowAsync<MessageNotFoundException>();
    }

    [Fact]
    public async Task Handle_NotOwner_ThrowsNoPermissionException()
    {
        var chat = await _h.SeedChat(memberUserIds: [1, 2]);
        var message = await _h.SeedMessage(chat.Id, 1, "original");
        var handler = CreateHandler(2);

        var act = async () => await handler.Handle(new EditMessageCommand
        {
            MessageId = message.Id,
            Text = "hacked"
        }, CancellationToken.None);

        await act.Should().ThrowAsync<NoPermissionException>();
    }

    [Fact]
    public async Task Handle_SystemMessage_ThrowsNoPermissionException()
    {
        var userId = 1L;
        var chat = await _h.SeedChat(memberUserIds: [userId, 2]);
        var message = await _h.SeedMessage(chat.Id, userId, "system", Domain.MessageContentType.System);
        var handler = CreateHandler(userId);

        var act = async () => await handler.Handle(new EditMessageCommand
        {
            MessageId = message.Id,
            Text = "edited"
        }, CancellationToken.None);

        await act.Should().ThrowAsync<NoPermissionException>();
    }

    [Fact]
    public async Task Handle_DeletedMessage_ThrowsMessageNotFoundException()
    {
        var userId = 1L;
        var chat = await _h.SeedChat(memberUserIds: [userId, 2]);
        var message = await _h.SeedMessage(chat.Id, userId, "deleted", isDeleted: true);
        var handler = CreateHandler(userId);

        var act = async () => await handler.Handle(new EditMessageCommand
        {
            MessageId = message.Id,
            Text = "edited"
        }, CancellationToken.None);

        await act.Should().ThrowAsync<MessageNotFoundException>();
    }

    [Fact]
    public async Task Handle_NoTextNoFiles_ThrowsMessageNotContainContextException()
    {
        var userId = 1L;
        var chat = await _h.SeedChat(memberUserIds: [userId, 2]);
        var message = await _h.SeedMessage(chat.Id, userId, "original");
        var handler = CreateHandler(userId);

        var act = async () => await handler.Handle(new EditMessageCommand
        {
            MessageId = message.Id
        }, CancellationToken.None);

        await act.Should().ThrowAsync<MessageNotContainContextException>();
    }

    [Fact]
    public async Task Handle_TextTooLong_ThrowsMessageTextTooLongException()
    {
        var userId = 1L;
        var chat = await _h.SeedChat(memberUserIds: [userId, 2]);
        var message = await _h.SeedMessage(chat.Id, userId, "original");
        var handler = CreateHandler(userId);

        var act = async () => await handler.Handle(new EditMessageCommand
        {
            MessageId = message.Id,
            Text = new string('a', 4097)
        }, CancellationToken.None);

        await act.Should().ThrowAsync<MessageTextTooLongException>();
    }

    [Fact]
    public async Task Handle_TooManyAttachments_ThrowsTooManyAttachmentsException()
    {
        var userId = 1L;
        var chat = await _h.SeedChat(memberUserIds: [userId, 2]);
        var message = await _h.SeedMessage(chat.Id, userId, "original");
        var handler = CreateHandler(userId);
        var fileIds = Enumerable.Range(0, 11).Select(_ => Guid.NewGuid()).ToList();

        var act = async () => await handler.Handle(new EditMessageCommand
        {
            MessageId = message.Id,
            FileIds = fileIds
        }, CancellationToken.None);

        await act.Should().ThrowAsync<TooManyAttachmentsException>();
    }

    [Fact]
    public async Task Handle_SetsIsEditedAndEditedAt()
    {
        var userId = 1L;
        var chat = await _h.SeedChat(memberUserIds: [userId, 2]);
        var message = await _h.SeedMessage(chat.Id, userId, "original");
        var handler = CreateHandler(userId);

        var result = await handler.Handle(new EditMessageCommand
        {
            MessageId = message.Id,
            Text = "edited"
        }, CancellationToken.None);

        result.Message.IsEdited.Should().BeTrue();
        result.Message.EditedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Handle_PublishesEditedEvent()
    {
        var userId = 1L;
        var chat = await _h.SeedChat(memberUserIds: [userId, 2]);
        var message = await _h.SeedMessage(chat.Id, userId, "original");
        var handler = CreateHandler(userId);

        await handler.Handle(new EditMessageCommand
        {
            MessageId = message.Id,
            Text = "edited"
        }, CancellationToken.None);

        _h.PublishEndpointMock.Verify(p => p.Publish(It.IsAny<Shared.Queue.Messages.MessageEditedEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_FederatedChat_PublishesEditedEventWithFederatedFields()
    {
        // Исходящий fed-путь (этап 2.4): правка своего сообщения в fed-DM должна нести FederatedId/
        // SenderUuid/RemoteParticipants, чтобы консюмер Federation положил её в outbox.
        var userId = 1L;
        var localUuid = Guid.NewGuid();
        var remoteUuid = Guid.NewGuid();
        var chat = await _h.SeedFederatedChat(userId, localUuid, remoteUuid, "remote.test");
        var federatedId = Guid.NewGuid();
        var message = await _h.SeedFederatedMessage(chat.Id, federatedId, senderUuid: localUuid, senderId: userId, text: "original");
        var handler = CreateHandler(userId);

        await handler.Handle(new EditMessageCommand
        {
            MessageId = message.Id,
            Text = "edited"
        }, CancellationToken.None);

        _h.PublishEndpointMock.Verify(p => p.Publish(
            It.Is<Shared.Queue.Messages.MessageEditedEvent>(e =>
                e.IsFederated
                && e.FederatedId == federatedId
                && e.SenderUuid == localUuid
                && e.RemoteParticipants.Count == 1
                && e.RemoteParticipants[0].Uuid == remoteUuid),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
