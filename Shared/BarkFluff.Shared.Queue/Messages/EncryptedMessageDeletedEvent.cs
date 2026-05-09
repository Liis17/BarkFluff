namespace BarkFluff.Shared.Queue.Messages;

public class EncryptedMessageDeletedEvent
{
    public Guid ChatId { get; set; }

    public List<long> ChatMembers { get; set; } = new();

    public long MessageId { get; set; }
}
