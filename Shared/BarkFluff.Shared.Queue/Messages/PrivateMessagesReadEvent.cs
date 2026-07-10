namespace BarkFluff.Shared.Queue.Messages;

public class PrivateMessagesReadEvent
{
    public Guid ChatId { get; set; }

    public long UserId { get; set; }

    public long LastReadMessageId { get; set; }

    public List<long> ChatMembers { get; set; } = [];
}
