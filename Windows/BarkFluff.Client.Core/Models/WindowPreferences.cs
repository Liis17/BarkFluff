namespace BarkFluff.Client.Core.Models;

public sealed record WindowPreferences
{
    public const int DefaultWidth = 1000;
    public const int DefaultHeight = 800;

    public bool RememberSize { get; init; } = true;
    public int Width { get; init; } = DefaultWidth;
    public int Height { get; init; } = DefaultHeight;
    public int? PositionX { get; init; }
    public int? PositionY { get; init; }
}
