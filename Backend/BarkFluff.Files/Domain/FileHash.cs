using System.ComponentModel.DataAnnotations;

namespace BarkFluff.Files.Domain;

/// <summary>
/// Represents a SHA256 hash of an uploaded file for deduplication purposes.
/// </summary>
public class FileHash
{
    [Key]
    public Guid FileId { get; set; }

    /// <summary>
    /// SHA256 hash of the file content (hex string, 64 characters).
    /// </summary>
    [Required]
    [MaxLength(64)]
    public string Hash { get; set; } = string.Empty;
}
