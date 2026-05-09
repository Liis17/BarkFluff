namespace BarkFluff.Shared.Queue.Messages;

public class MessagePinnedEvent
{
    public Guid ChatId { get; set; }

    public List<long> ChatMembers { get; set; }

    public long MessageId { get; set; }

    public long PinnerUserId { get; set; }

    public DateTime PinnedAt { get; set; }
}
