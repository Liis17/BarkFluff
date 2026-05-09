namespace BarkFluff.Shared.Queue.Messages;

/// <summary>
/// Ответ устройства-получателя на инвайт секретного чата.
/// Updates пушит в стрим SubscribeSecretChatResolutions устройства-инициатора (sender_device_id).
/// При accepted=true может содержать первое ответное SignalMessage от получателя.
/// </summary>
public class SecretChatInviteResolutionEvent
{
    public string InviteId { get; set; } = string.Empty;

    public long SenderUserId { get; set; }

    public Guid SenderDeviceId { get; set; }

    public long RecipientUserId { get; set; }

    public Guid RecipientDeviceId { get; set; }

    public bool Accepted { get; set; }

    public byte[] ResponseEnvelope { get; set; } = Array.Empty<byte>();
}
