using System.ComponentModel.DataAnnotations;

namespace BarkFluff.Files.Domain;

public class BadgeImage
{
    [Key]
    public Guid Id { get; set; }

    [Required]
    public string Filename { get; set; } = string.Empty;

    public long Size { get; set; }

    public string? Etag { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? UploadedAt { get; set; }
}
