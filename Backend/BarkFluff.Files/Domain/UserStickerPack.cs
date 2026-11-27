namespace BarkFluff.Files.Domain;

/// <summary>Набор стикеров, добавленный пользователем к себе.</summary>
public class UserStickerPack
{
    public long UserId { get; set; }

    public Guid StickerPackId { get; set; }

    public DateTime AddedAt { get; set; }

    public StickerPack StickerPack { get; set; } = null!;
}
