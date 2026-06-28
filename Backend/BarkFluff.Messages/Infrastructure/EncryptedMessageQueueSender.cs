namespace BarkFluff.Messages.Infrastructure;

using Domain;

using Google.Protobuf;

using MassTransit;

using Mapping;

using Shared.Queue.Messages;

/// <summary>
/// Публикует события приватных (E2E через passphrase) шифрованных сообщений.
/// Updates слушает их и рассылает участникам приватного чата (user-scope).
/// </summary>
public class EncryptedMessageQueueSender
{
    private readonly IPublishEndpoint _publishEndpoint;

    public EncryptedMessageQueueSender(IPublishEndpoint publishEndpoint)
    {
        _publishEndpoint = publishEndpoint;
    }

    public virtual async Task SendNew(EncryptedMessage message, List<long> chatMembers)
    {
        var evt = new NewEncryptedMessageEvent
        {
            ChatId = message.ChatId,
            ChatMembers = chatMembers,
            Message = message.ToGrpc().ToByteArray()
        };

        await _publishEndpoint.Publish(evt);
    }

    public virtual async Task SendEdited(EncryptedMessage message, List<long> chatMembers)
    {
        var evt = new EncryptedMessageEditedEvent
        {
            ChatId = message.ChatId,
            ChatMembers = chatMembers,
            Message = message.ToGrpc().ToByteArray()
        };

        await _publishEndpoint.Publish(evt);
    }

    public virtual async Task SendDeleted(Guid chatId, long messageId, List<long> chatMembers)
    {
        var evt = new EncryptedMessageDeletedEvent
        {
            ChatId = chatId,
            ChatMembers = chatMembers,
            MessageId = messageId
        };

        await _publishEndpoint.Publish(evt);
    }

    public virtual async Task SendInvite(
        Guid chatId,
        long inviterUserId,
        long inviteeUserId,
        byte[] kdfSalt,
        byte[] passphraseVerifier,
        DateTime invitedAt)
    {
        var evt = new PrivateChatInviteEvent
        {
            ChatId = chatId,
            InviterUserId = inviterUserId,
            InviteeUserId = inviteeUserId,
            KdfSalt = kdfSalt,
            PassphraseVerifier = passphraseVerifier,
            InvitedAt = invitedAt
        };

        await _publishEndpoint.Publish(evt);
    }

    public virtual async Task SendInviteResolution(
        Guid chatId,
        long inviterUserId,
        long inviteeUserId,
        bool accepted)
    {
        var evt = new PrivateChatInviteResolutionEvent
        {
            ChatId = chatId,
            InviterUserId = inviterUserId,
            InviteeUserId = inviteeUserId,
            Accepted = accepted
        };

        await _publishEndpoint.Publish(evt);
    }
}
