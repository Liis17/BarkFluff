using System.ComponentModel.DataAnnotations;

namespace BarkFluff.Files.Domain;

public class UploadFile
{

    [Key]
    public Guid Id { get; set; }

    /// <summary>
    /// List of user IDs who uploaded this file (for deduplication tracking).
    /// </summary>
    public List<long> Uploaders { get; set; } = new();

    public DateTime CreatedAt { get; set; }

    public DateTime? UploadedAt { get; set; }

    public string? Etag { get; set; }

    public UploadFileType Type { get; set; }

    public string? Filename { get; set; }

    public Guid? PreviewId { get; set; }

    public long Size { get; set; }
}