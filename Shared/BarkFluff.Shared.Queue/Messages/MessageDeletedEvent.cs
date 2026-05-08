namespace BarkFluff.Shared.Queue.Messages;

public class MessageDeletedEvent
{
    public Guid ChatId { get; set; }

    public List<long> ChatMembers { get; set; }

    public long MessageId { get; set; }
}
