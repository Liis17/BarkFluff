namespace BarkFluff.Shared.Queue.Messages;

/// <summary>
/// Событие нового шифрованного сообщения в приватном чате.
/// Updates рассылает всем участникам чата (user-scoped).
/// </summary>
public class NewEncryptedMessageEvent
{
    public Guid ChatId { get; set; }

    public List<long> ChatMembers { get; set; } = new();

    /// <summary>
    /// Сериализованный proto barkfluff.shared.EncryptedMessage.
    /// </summary>
    public byte[] Message { get; set; } = Array.Empty<byte>();
}
