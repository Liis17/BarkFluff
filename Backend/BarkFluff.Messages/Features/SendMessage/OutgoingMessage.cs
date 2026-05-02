namespace BarkFluff.Messages.Features.SendMessage;

public class OutgoingMessage
{
    public string? Text { get; set; }

    public List<Guid>? FileIds { get; set; }

    public long? ForwardedMessageId { get; set; }
}