namespace BarkFluff.Shared.Queue.Messages;

public class AllMessagesUnpinnedEvent
{
    public Guid ChatId { get; set; }

    public List<long> ChatMembers { get; set; }
}
