namespace BarkFluff.Messages.Infrastructure;

using MassTransit;

using Shared.Queue.Messages;

/// <summary>
/// Публикует события секретных чатов (Signal Double Ratchet).
/// Updates слушает их и доставляет на конкретное устройство получателя (device-scope).
/// Содержимое envelope opaque — сервис только релэит.
/// Для оффлайн-получателя envelope уже лежит в Redis-буфере (SecretMessageBuffer);
/// здесь же отдельно публикуется PushNotificationEvent без содержимого.
/// </summary>
public class SecretMessageQueueSender
{
    private readonly IPublishEndpoint _publishEndpoint;

    public SecretMessageQueueSender(IPublishEndpoint publishEndpoint)
    {
        _publishEndpoint = publishEndpoint;
    }

    public virtual async Task SendInvite(
        string inviteId,
        long senderUserId,
        Guid senderDeviceId,
        long recipientUserId,
        Guid recipientDeviceId,
        byte[] initialEnvelope,
        DateTime sentAt)
    {
        var evt = new SecretChatInviteEvent
        {
            InviteId = inviteId,
            SenderUserId = senderUserId,
            SenderDeviceId = senderDeviceId,
            RecipientUserId = recipientUserId,
            RecipientDeviceId = recipientDeviceId,
            InitialEnvelope = initialEnvelope,
            SentAt = sentAt
        };

        await _publishEndpoint.Publish(evt);
    }

    public virtual async Task SendMessage(
        string messageId,
        long senderUserId,
        Guid senderDeviceId,
        long recipientUserId,
        Guid recipientDeviceId,
        byte[] envelope,
        DateTime sentAt)
    {
        var evt = new NewSecretMessageEvent
        {
            MessageId = messageId,
            SenderUserId = senderUserId,
            SenderDeviceId = senderDeviceId,
            RecipientUserId = recipientUserId,
            RecipientDeviceId = recipientDeviceId,
            Envelope = envelope,
            SentAt = sentAt
        };

        await _publishEndpoint.Publish(evt);
    }

    public virtual async Task SendInviteResolution(
        string inviteId,
        long senderUserId,
        Guid senderDeviceId,
        long recipientUserId,
        Guid recipientDeviceId,
        bool accepted,
        byte[] responseEnvelope)
    {
        var evt = new SecretChatInviteResolutionEvent
        {
            InviteId = inviteId,
            SenderUserId = senderUserId,
            SenderDeviceId = senderDeviceId,
            RecipientUserId = recipientUserId,
            RecipientDeviceId = recipientDeviceId,
            Accepted = accepted,
            ResponseEnvelope = responseEnvelope
        };

        await _publishEndpoint.Publish(evt);
    }

    /// <summary>
    /// Push без содержимого: только «вам пришло секретное сообщение».
    /// Использует существующий PushNotificationEvent с пустыми полями content.
    /// </summary>
    public virtual async Task SendSilentPush(long recipientUserId, string contentLabel)
    {
        var push = new PushNotificationEvent
        {
            ChatId = Guid.Empty,
            SenderId = 0,
            MessageId = 0,
            MessageText = contentLabel,
            RecipientUserIds = new List<long> { recipientUserId },
            ContentType = 0,
            AttachmentCount = 0,
            IsGroupChat = false
        };

        await _publishEndpoint.Publish(push);
    }
}
