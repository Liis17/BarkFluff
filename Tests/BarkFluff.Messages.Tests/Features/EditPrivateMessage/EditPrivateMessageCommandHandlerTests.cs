using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Messages.Features.EditPrivateMessage;
using BarkFluff.Messages.Infrastructure;
using BarkFluff.Messages.Persistence.Services;
using BarkFluff.Proto.Messages;
using BarkFluff.Shared.Exceptions.Messages;

namespace BarkFluff.Messages.Tests.Features.EditPrivateMessage;

public class EditPrivateMessageCommandHandlerTests
{
    private readonly TestHelper _h = new();
    private readonly EncryptedMessageQueueSender _queueSender;

    public EditPrivateMessageCommandHandlerTests()
    {
        _queueSender = new EncryptedMessageQueueSender(_h.PublishEndpointMock.Object);
    }

    private EditPrivateMessageCommandHandler CreateHandler(long userId)
    {
        return new EditPrivateMessageCommandHandler(
            _h.ChatsStorage,
            _h.EncryptedMessagesStorage,
            _queueSender,
            _h.CreateUserContext(userId),
            _h.Metrics,
            TestHelper.CreateLogger<EditPrivateMessageCommandHandler>());
    }

    [Fact]
    public async Task Handle_ValidEdit_UpdatesMessage()
    {
        var userId = 1L;
        var chat = await _h.SeedChat(type: Domain.ChatType.Private, memberUserIds: [userId, 2], kdfSalt: new byte[32], passphraseVerifier: new byte[32]);
        var msg = await _h.SeedEncryptedMessage(chat.Id, userId, Guid.NewGuid());
        var handler = CreateHandler(userId);

        var result = await handler.Handle(new EditPrivateMessageCommand
        {
            MessageId = msg.Id,
            Ciphertext = new byte[200],
            Nonce = new byte[12],
            AssociatedData = Array.Empty<byte>()
        }, CancellationToken.None);

        result.Message.IsEdited.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_CiphertextTooLarge_ThrowsInvalidEncryptedPayloadException()
    {
        var handler = CreateHandler(1);

        var act = async () => await handler.Handle(new EditPrivateMessageCommand
        {
            MessageId = 1,
            Ciphertext = new byte[65 * 1024],
            Nonce = new byte[12]
        }, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidEncryptedPayloadException>();
    }

    [Fact]
    public async Task Handle_NonceTooShort_ThrowsInvalidEncryptedPayloadException()
    {
        var handler = CreateHandler(1);

        var act = async () => await handler.Handle(new EditPrivateMessageCommand
        {
            MessageId = 1,
            Ciphertext = new byte[100],
            Nonce = new byte[11]
        }, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidEncryptedPayloadException>();
    }

    [Fact]
    public async Task Handle_MessageNotFound_ThrowsEncryptedMessageNotFoundException()
    {
        var handler = CreateHandler(1);

        var act = async () => await handler.Handle(new EditPrivateMessageCommand
        {
            MessageId = 99999,
            Ciphertext = new byte[100],
            Nonce = new byte[12]
        }, CancellationToken.None);

        await act.Should().ThrowAsync<EncryptedMessageNotFoundException>();
    }

    [Fact]
    public async Task Handle_DeletedMessage_ThrowsEncryptedMessageNotFoundException()
    {
        var chat = await _h.SeedChat(type: Domain.ChatType.Private, memberUserIds: [1, 2], kdfSalt: new byte[32], passphraseVerifier: new byte[32]);
        var msg = await _h.SeedEncryptedMessage(chat.Id, 1, Guid.NewGuid(), isDeleted: true);
        var handler = CreateHandler(1);

        var act = async () => await handler.Handle(new EditPrivateMessageCommand
        {
            MessageId = msg.Id,
            Ciphertext = new byte[100],
            Nonce = new byte[12]
        }, CancellationToken.None);

        await act.Should().ThrowAsync<EncryptedMessageNotFoundException>();
    }

    [Fact]
    public async Task Handle_NotOwner_ThrowsNoPermissionException()
    {
        var chat = await _h.SeedChat(type: Domain.ChatType.Private, memberUserIds: [1, 2], kdfSalt: new byte[32], passphraseVerifier: new byte[32]);
        var msg = await _h.SeedEncryptedMessage(chat.Id, 1, Guid.NewGuid());
        var handler = CreateHandler(2);

        var act = async () => await handler.Handle(new EditPrivateMessageCommand
        {
            MessageId = msg.Id,
            Ciphertext = new byte[100],
            Nonce = new byte[12]
        }, CancellationToken.None);

        await act.Should().ThrowAsync<NoPermissionException>();
    }

    [Fact]
    public async Task Handle_PublishesEditedEvent()
    {
        var userId = 1L;
        var chat = await _h.SeedChat(type: Domain.ChatType.Private, memberUserIds: [userId, 2], kdfSalt: new byte[32], passphraseVerifier: new byte[32]);
        var msg = await _h.SeedEncryptedMessage(chat.Id, userId, Guid.NewGuid());
        var handler = CreateHandler(userId);

        await handler.Handle(new EditPrivateMessageCommand
        {
            MessageId = msg.Id,
            Ciphertext = new byte[100],
            Nonce = new byte[12],
            AssociatedData = Array.Empty<byte>()
        }, CancellationToken.None);

        _h.PublishEndpointMock.Verify(p => p.Publish(It.IsAny<Shared.Queue.Messages.EncryptedMessageEditedEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
