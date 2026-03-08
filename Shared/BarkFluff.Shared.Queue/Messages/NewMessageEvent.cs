namespace BarkFluff.Shared.Queue.Messages;

public class NewMessageEvent
{
    public Guid ChatId { get; set; }

    public List<long> ChatMembers { get; set; }

    public byte[] Message { get; set; }
}