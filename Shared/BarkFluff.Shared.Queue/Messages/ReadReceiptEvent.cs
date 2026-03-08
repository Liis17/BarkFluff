namespace BarkFluff.Shared.Queue.Messages;

public class ReadReceiptEvent
{
    public Guid ChatId { get; set; }

    public long MessageId { get; set; }

    public List<long> ReadBy { get; set; }

    public List<long> ChatMembers { get; set; }

    public bool IsLastMessage { get; set; }

    public DateTime ReadAt { get; set; }
}
