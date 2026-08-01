using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Messages.Features.SendPrivateMessage;
using BarkFluff.Messages.Infrastructure;
using BarkFluff.Messages.Persistence.Services;
using BarkFluff.Proto.Messages;
using BarkFluff.Shared.Exceptions.Messages;

namespace BarkFluff.Messages.Tests.Features.SendPrivateMessage;

public class SendPrivateMessageCommandHandlerTests
{
    private readonly TestHelper _h = new();
    private readonly EncryptedMessageQueueSender _queueSender;

    public SendPrivateMessageCommandHandlerTests()
    {
        _queueSender = new EncryptedMessageQueueSender(_h.PublishEndpointMock.Object);
    }

    private SendPrivateMessageCommandHandler CreateHandler(long userId, string? deviceId = "00000000-0000-0000-0000-000000000001")
    {
        return new SendPrivateMessageCommandHandler(
            _h.ChatsStorage,
            _h.EncryptedMessagesStorage,
            _queueSender,
            _h.CreateUserContext(userId, deviceId),
            _h.Metrics,
            TestHelper.CreateLogger<SendPrivateMessageCommandHandler>());
    }

    [Fact]
    public async Task Handle_ValidMessage_SendsAndReturnsResponse()
    {
        var userId = 1L;
        var chat = await _h.SeedChat(type: Domain.ChatType.Private, memberUserIds: [userId, 2], kdfSalt: new byte[32], passphraseVerifier: new byte[32], privateInviteState: Domain.PrivateChatInviteState.Accepted);
        var handler = CreateHandler(userId);

        var result = await handler.Handle(new SendPrivateMessageCommand
        {
            ChatId = chat.Id,
            Ciphertext = new byte[100],
            Nonce = new byte[12],
            AssociatedData = Array.Empty<byte>()
        }, CancellationToken.None);

        result.Should().NotBeNull();
        result.Message.Should().NotBeNull();
    }

    [Fact]
    public async Task Handle_NoDeviceId_ThrowsDeviceIdRequiredException()
    {
        var handler = CreateHandler(1, deviceId: null);

        var act = async () => await handler.Handle(new SendPrivateMessageCommand
        {
            ChatId = Guid.NewGuid(),
            Ciphertext = new byte[100],
            Nonce = new byte[12]
        }, CancellationToken.None);

        await act.Should().ThrowAsync<DeviceIdRequiredException>();
    }

    [Fact]
    public async Task Handle_InvalidDeviceId_ThrowsDeviceIdRequiredException()
    {
        var handler = CreateHandler(1, deviceId: "not-a-guid");

        var act = async () => await handler.Handle(new SendPrivateMessageCommand
        {
            ChatId = Guid.NewGuid(),
            Ciphertext = new byte[100],
            Nonce = new byte[12]
        }, CancellationToken.None);

        await act.Should().ThrowAsync<DeviceIdRequiredException>();
    }

    [Fact]
    public async Task Handle_CiphertextTooLarge_ThrowsInvalidEncryptedPayloadException()
    {
        var handler = CreateHandler(1);

        var act = async () => await handler.Handle(new SendPrivateMessageCommand
        {
            ChatId = Guid.NewGuid(),
            Ciphertext = new byte[65 * 1024],
            Nonce = new byte[12]
        }, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidEncryptedPayloadException>();
    }

    [Fact]
    public async Task Handle_EmptyCiphertext_ThrowsInvalidEncryptedPayloadException()
    {
        var handler = CreateHandler(1);

        var act = async () => await handler.Handle(new SendPrivateMessageCommand
        {
            ChatId = Guid.NewGuid(),
            Ciphertext = Array.Empty<byte>(),
            Nonce = new byte[12]
        }, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidEncryptedPayloadException>();
    }

    [Fact]
    public async Task Handle_NonceTooShort_ThrowsInvalidEncryptedPayloadException()
    {
        var handler = CreateHandler(1);

        var act = async () => await handler.Handle(new SendPrivateMessageCommand
        {
            ChatId = Guid.NewGuid(),
            Ciphertext = new byte[100],
            Nonce = new byte[11]
        }, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidEncryptedPayloadException>();
    }

    [Fact]
    public async Task Handle_NonceTooLong_ThrowsInvalidEncryptedPayloadException()
    {
        var handler = CreateHandler(1);

        var act = async () => await handler.Handle(new SendPrivateMessageCommand
        {
            ChatId = Guid.NewGuid(),
            Ciphertext = new byte[100],
            Nonce = new byte[33]
        }, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidEncryptedPayloadException>();
    }

    [Fact]
    public async Task Handle_AadTooLarge_ThrowsInvalidEncryptedPayloadException()
    {
        var handler = CreateHandler(1);

        var act = async () => await handler.Handle(new SendPrivateMessageCommand
        {
            ChatId = Guid.NewGuid(),
            Ciphertext = new byte[100],
            Nonce = new byte[12],
            AssociatedData = new byte[5 * 1024]
        }, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidEncryptedPayloadException>();
    }

    [Fact]
    public async Task Handle_ChatNotFound_ThrowsChatNotFoundException()
    {
        var handler = CreateHandler(1);

        var act = async () => await handler.Handle(new SendPrivateMessageCommand
        {
            ChatId = Guid.NewGuid(),
            Ciphertext = new byte[100],
            Nonce = new byte[12]
        }, CancellationToken.None);

        await act.Should().ThrowAsync<ChatNotFoundException>();
    }

    [Fact]
    public async Task Handle_NotPrivateChat_ThrowsChatNotPrivateException()
    {
        var chat = await _h.SeedChat(memberUserIds: [1, 2]);
        var handler = CreateHandler(1);

        var act = async () => await handler.Handle(new SendPrivateMessageCommand
        {
            ChatId = chat.Id,
            Ciphertext = new byte[100],
            Nonce = new byte[12]
        }, CancellationToken.None);

        await act.Should().ThrowAsync<ChatNotPrivateException>();
    }

    [Fact]
    public async Task Handle_NotChatMember_ThrowsNoAccessToChatException()
    {
        var chat = await _h.SeedChat(type: Domain.ChatType.Private, memberUserIds: [99, 100], kdfSalt: new byte[32], passphraseVerifier: new byte[32]);
        var handler = CreateHandler(1);

        var act = async () => await handler.Handle(new SendPrivateMessageCommand
        {
            ChatId = chat.Id,
            Ciphertext = new byte[100],
            Nonce = new byte[12]
        }, CancellationToken.None);

        await act.Should().ThrowAsync<NoAccessToChatException>();
    }

    [Fact]
    public async Task Handle_PublishesNewEncryptedMessageEvent()
    {
        var userId = 1L;
        var chat = await _h.SeedChat(type: Domain.ChatType.Private, memberUserIds: [userId, 2], kdfSalt: new byte[32], passphraseVerifier: new byte[32], privateInviteState: Domain.PrivateChatInviteState.Accepted);
        var handler = CreateHandler(userId);

        await handler.Handle(new SendPrivateMessageCommand
        {
            ChatId = chat.Id,
            Ciphertext = new byte[100],
            Nonce = new byte[12],
            AssociatedData = Array.Empty<byte>()
        }, CancellationToken.None);

        _h.PublishEndpointMock.Verify(p => p.Publish(It.IsAny<Shared.Queue.Messages.NewEncryptedMessageEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
