using System.ComponentModel.DataAnnotations;

namespace BarkFluff.Messages.Domain;

public class MessageAttachment
{
    [Key]
    public long Id { get; set; }
    
    public MessageAttachmentType Type { get; set; }
    
    public string FileId { get; set; }
}