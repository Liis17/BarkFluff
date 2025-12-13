using System.ComponentModel.DataAnnotations;

namespace BarkFluff.Messages.Domain;

public class MessageAttachment
{
    [Key]
    public long Id { get; set; }
    
    public MessageAttachmentType Type { get; set; }
    
    public string FileId { get; set; }
    
    public string? PreviewUrl { get; set; }
    
    public string? PreviewFileId { get; set; }
    
    public string? FileName { get; set; }
    
    public long FileSize { get; set; }
}