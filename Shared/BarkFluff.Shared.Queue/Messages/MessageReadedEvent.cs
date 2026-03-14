namespace BarkFluff.Shared.Queue.Messages;

public class MessageReadEvent
{
    public Guid ChatId { get; set; }

    public long MessageId { get; set; }

    public List<long> NewReadBy { get; set; }

    public List<long> ChatMembers { get; set; }
    
}