using System.ComponentModel.DataAnnotations;

namespace BarkFluff.Messages.Domain;

public class EncryptedMessage
{
    [Key]
    public long Id { get; set; }

    public Guid ChatId { get; set; }

    public long SenderId { get; set; }

    public Guid SenderDeviceId { get; set; }

    public DateTime SentAt { get; set; }

    public byte[] Ciphertext { get; set; } = Array.Empty<byte>();

    public byte[] Nonce { get; set; } = Array.Empty<byte>();

    public byte[] AssociatedData { get; set; } = Array.Empty<byte>();

    public bool IsEdited { get; set; }

    public DateTime? EditedAt { get; set; }

    public bool IsDeleted { get; set; }
}
