using System.ComponentModel.DataAnnotations;

namespace BarkFluff.Messages.Domain;

public class MessageAttachment
{
    [Key]
    public long Id { get; set; }

    public MessageAttachmentType Type { get; set; }

    public string? FileId { get; set; }

    public string? PreviewUrl { get; set; }

    public long FileSize { get; set; }

    public string? ForwardedAuthorName { get; set; }

    public long? ForwardedOriginalMessageId { get; set; }

    public string? ForwardedText { get; set; }

    public List<ForwardedMessageAttachment>? ForwardedAttachments { get; set; }
}