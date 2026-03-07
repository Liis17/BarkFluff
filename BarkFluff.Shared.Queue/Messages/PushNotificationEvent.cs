namespace BarkFluff.Shared.Queue.Messages;

public class PushNotificationEvent
{
    public Guid ChatId { get; set; }

    public long SenderId { get; set; }

    public long MessageId { get; set; }

    public string? MessageText { get; set; }

    public List<long> RecipientUserIds { get; set; } = [];
}
