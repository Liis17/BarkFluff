using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Messages.Features.DeletePrivateMessage;
using BarkFluff.Messages.Infrastructure;
using BarkFluff.Messages.Persistence.Services;
using BarkFluff.Proto.Messages;
using BarkFluff.Shared.Exceptions.Messages;

namespace BarkFluff.Messages.Tests.Features.DeletePrivateMessage;

public class DeletePrivateMessageCommandHandlerTests
{
    private readonly TestHelper _h = new();
    private readonly EncryptedMessageQueueSender _queueSender;

    public DeletePrivateMessageCommandHandlerTests()
    {
        _queueSender = new EncryptedMessageQueueSender(_h.PublishEndpointMock.Object);
    }

    private DeletePrivateMessageCommandHandler CreateHandler(long userId)
    {
        return new DeletePrivateMessageCommandHandler(
            _h.ChatsStorage,
            _h.EncryptedMessagesStorage,
            _queueSender,
            _h.CreateUserContext(userId),
            _h.Metrics,
            TestHelper.CreateLogger<DeletePrivateMessageCommandHandler>());
    }

    [Fact]
    public async Task Handle_ValidDelete_SoftDeletesMessage()
    {
        var userId = 1L;
        var chat = await _h.SeedChat(type: Domain.ChatType.Private, memberUserIds: [userId, 2], kdfSalt: new byte[32], passphraseVerifier: new byte[32]);
        var msg = await _h.SeedEncryptedMessage(chat.Id, userId, Guid.NewGuid());
        var handler = CreateHandler(userId);

        var result = await handler.Handle(new DeletePrivateMessageCommand { MessageId = msg.Id }, CancellationToken.None);

        result.Should().NotBeNull();
        var dbMsg = await _h.EncryptedMessagesStorage.GetByIdAsync(msg.Id);
        dbMsg!.IsDeleted.Should().BeTrue();
        dbMsg.Ciphertext.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_MessageNotFound_ThrowsEncryptedMessageNotFoundException()
    {
        var handler = CreateHandler(1);

        var act = async () => await handler.Handle(new DeletePrivateMessageCommand { MessageId = 99999 }, CancellationToken.None);

        await act.Should().ThrowAsync<EncryptedMessageNotFoundException>();
    }

    [Fact]
    public async Task Handle_NotOwner_ThrowsNoPermissionException()
    {
        var chat = await _h.SeedChat(type: Domain.ChatType.Private, memberUserIds: [1, 2], kdfSalt: new byte[32], passphraseVerifier: new byte[32]);
        var msg = await _h.SeedEncryptedMessage(chat.Id, 1, Guid.NewGuid());
        var handler = CreateHandler(2);

        var act = async () => await handler.Handle(new DeletePrivateMessageCommand { MessageId = msg.Id }, CancellationToken.None);

        await act.Should().ThrowAsync<NoPermissionException>();
    }

    [Fact]
    public async Task Handle_AlreadyDeleted_ReturnsIdempotentResponse()
    {
        var userId = 1L;
        var chat = await _h.SeedChat(type: Domain.ChatType.Private, memberUserIds: [userId, 2], kdfSalt: new byte[32], passphraseVerifier: new byte[32]);
        var msg = await _h.SeedEncryptedMessage(chat.Id, userId, Guid.NewGuid(), isDeleted: true);
        var handler = CreateHandler(userId);

        var result = await handler.Handle(new DeletePrivateMessageCommand { MessageId = msg.Id }, CancellationToken.None);

        result.Should().NotBeNull();
        _h.PublishEndpointMock.Verify(p => p.Publish(It.IsAny<Shared.Queue.Messages.EncryptedMessageDeletedEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_PublishesDeletedEvent()
    {
        var userId = 1L;
        var chat = await _h.SeedChat(type: Domain.ChatType.Private, memberUserIds: [userId, 2], kdfSalt: new byte[32], passphraseVerifier: new byte[32]);
        var msg = await _h.SeedEncryptedMessage(chat.Id, userId, Guid.NewGuid());
        var handler = CreateHandler(userId);

        await handler.Handle(new DeletePrivateMessageCommand { MessageId = msg.Id }, CancellationToken.None);

        _h.PublishEndpointMock.Verify(p => p.Publish(It.IsAny<Shared.Queue.Messages.EncryptedMessageDeletedEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
