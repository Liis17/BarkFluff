namespace BarkFluff.Shared.Queue.Messages;

/// <summary>
/// Новое секретное сообщение (Signal Double Ratchet).
/// Updates маршрутизирует по device-scope подписке именно на RecipientDeviceId.
/// Если устройство оффлайн — Updates ничего не делает: envelope уже лежит в Redis-буфере (24ч).
/// PushNotificationEvent для push без содержимого публикуется отдельно.
/// </summary>
public class NewSecretMessageEvent
{
    public string MessageId { get; set; } = string.Empty;

    public long SenderUserId { get; set; }

    public Guid SenderDeviceId { get; set; }

    public long RecipientUserId { get; set; }

    public Guid RecipientDeviceId { get; set; }

    /// <summary>
    /// Opaque libsignal SignalMessage (включая ratchet headers + ciphertext).
    /// Сервер payload не интерпретирует.
    /// </summary>
    public byte[] Envelope { get; set; } = Array.Empty<byte>();

    public DateTime SentAt { get; set; }
}
