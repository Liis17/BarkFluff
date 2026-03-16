using System.ComponentModel.DataAnnotations;

namespace BarkFluff.Files.Domain;

public class Sticker
{
    [Key]
    public Guid Id { get; set; }

    public Guid StickerPackId { get; set; }

    public Guid FileId { get; set; }

    public Guid? PreviewFileId { get; set; }

    [Required]
    public string Emoji { get; set; } = string.Empty;

    public DateTime AddedAt { get; set; }

    public StickerPack StickerPack { get; set; } = null!;
}
