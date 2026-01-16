using BarkFluff.Messages.Domain;

namespace BarkFluff.Messages.Persistence.Services.Dtos;

public class ChatAttachmentDto
{
    public long MessageId { get; set; }
    public long SenderId { get; set; }
    public DateTime SentAt { get; set; }
    public long AttachmentId { get; set; }
    public MessageAttachmentType AttachmentType { get; set; }
    public string FileId { get; set; }
    public string? PreviewUrl { get; set; }
    public long FileSize { get; set; }
}
