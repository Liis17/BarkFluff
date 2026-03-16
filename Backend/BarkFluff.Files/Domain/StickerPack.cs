using System.ComponentModel.DataAnnotations;

namespace BarkFluff.Files.Domain;

public class StickerPack
{
    [Key]
    public Guid Id { get; set; }

    public long CreatorUserId { get; set; }

    public Guid? CoverStickerId { get; set; }

    [Required]
    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public List<Sticker> Stickers { get; set; } = new();
}
