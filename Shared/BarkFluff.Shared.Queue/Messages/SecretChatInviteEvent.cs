namespace BarkFluff.Shared.Queue.Messages;

/// <summary>
/// Инвайт секретного чата. Updates рассылает по device-scope подписке именно на RecipientDeviceId.
/// Содержит initial X3DH PreKeySignalMessage в InitialEnvelope.
/// </summary>
public class SecretChatInviteEvent
{
    public string InviteId { get; set; } = string.Empty;

    public long SenderUserId { get; set; }

    public Guid SenderDeviceId { get; set; }

    public long RecipientUserId { get; set; }

    public Guid RecipientDeviceId { get; set; }

    public byte[] InitialEnvelope { get; set; } = Array.Empty<byte>();

    public DateTime SentAt { get; set; }
}
