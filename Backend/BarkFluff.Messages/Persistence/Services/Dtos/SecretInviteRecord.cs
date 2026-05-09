namespace BarkFluff.Messages.Persistence.Services.Dtos;

public class SecretInviteRecord
{
    public string InviteId { get; set; } = string.Empty;

    public long SenderUserId { get; set; }

    public Guid SenderDeviceId { get; set; }

    public long RecipientUserId { get; set; }

    public Guid RecipientDeviceId { get; set; }

    public byte[] InitialEnvelope { get; set; } = Array.Empty<byte>();

    public DateTime SentAt { get; set; }
}
