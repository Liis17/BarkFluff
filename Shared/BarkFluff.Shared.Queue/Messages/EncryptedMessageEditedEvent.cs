namespace BarkFluff.Shared.Queue.Messages;

public class EncryptedMessageEditedEvent
{
    public Guid ChatId { get; set; }

    public List<long> ChatMembers { get; set; } = new();

    /// <summary>
    /// Сериализованный proto barkfluff.shared.EncryptedMessage с обновлённым ciphertext.
    /// </summary>
    public byte[] Message { get; set; } = Array.Empty<byte>();
}
