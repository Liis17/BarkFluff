namespace BarkFluff.Shared.Queue.Messages;

public class MessageUnpinnedEvent
{
    public Guid ChatId { get; set; }

    public List<long> ChatMembers { get; set; }

    public long MessageId { get; set; }
}
