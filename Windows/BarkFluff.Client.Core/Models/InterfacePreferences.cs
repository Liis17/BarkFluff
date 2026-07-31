namespace BarkFluff.Client.Core.Models;

public sealed record InterfacePreferences
{
    public int ChatCornerRadius { get; init; } = 20;
    public bool ChatBackgroundBlur { get; init; }
    public int ChatBackgroundBlurRadius { get; init; } = 10;
    public int ChatBackgroundDim { get; init; }
    public string? ChatBackgroundFileId { get; init; }
    public bool FoldersCompact { get; init; }
    public bool FoldersNoOutline { get; init; }
    public bool FoldersExcludeFromAll { get; init; }
    public bool RelativeOnlineTime { get; init; } = true;
    public int ChatStickerSizeDp { get; init; } = 160;
}
