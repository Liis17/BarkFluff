using System.ComponentModel.DataAnnotations;

namespace BarkFluff.Files.Domain;

public class UploadFile
{

    [Key]
    public Guid Id { get; set; }
    
    public long Uploader { get; set; }
    
    public DateTime CreatedAt { get; set; }
    
    public DateTime? UploadedAt { get; set; }
    
    public string? Etag { get; set; }
    
    public UploadFileType Type { get; set; }

    public string? Filename { get; set; }
}