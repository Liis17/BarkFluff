namespace BarkFluff.Messages.Domain;

public class ForwardedMessageAttachment
{
    public long Id { get; set; }

    public MessageAttachmentType Type { get; set; }

    public string FileId { get; set; }

    public string? PreviewUrl { get; set; }

    public long FileSize { get; set; }
}
