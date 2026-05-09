namespace BarkFluff.Messages.Persistence.Services.Dtos;

public class SecretMessageRecord
{
    public string MessageId { get; set; } = string.Empty;

    public long SenderUserId { get; set; }

    public Guid SenderDeviceId { get; set; }

    public Guid RecipientDeviceId { get; set; }

    public byte[] Envelope { get; set; } = Array.Empty<byte>();

    public DateTime SentAt { get; set; }
}
