namespace BarkFluff.Messages.Domain;

public class ChatDraft
{
    public Guid ChatId { get; set; }

    public long UserId { get; set; }

    public string Text { get; set; } = string.Empty;

    public long? ReplyToMessageId { get; set; }

    public DateTime UpdatedAt { get; set; }

    public Guid Revision { get; set; }

    public Chat? Chat { get; set; }
}
