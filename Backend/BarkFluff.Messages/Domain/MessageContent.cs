namespace BarkFluff.Messages.Domain;

public class MessageContent
{
    public string? Text { get; set; }
    
    public List<MessageAttachment>? Attachments { get; set; }
}