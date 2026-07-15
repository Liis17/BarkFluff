namespace BarkFluff.Shared.Queue.Messages;

public class ChatMemberKickedEvent
{
    public Guid ChatId { get; set; }

    public long UserId { get; set; }
}
