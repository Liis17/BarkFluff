namespace BarkFluff.Client.Core.Models;

public static class MessageFeedViewport
{
    private const double EdgeThreshold = 48;

    public static bool IsNearEdge(double distance) => distance <= EdgeThreshold;

    public static bool HasScrolled(
        (double Offset, double Extent, double Viewport) previous,
        (double Offset, double Extent, double Viewport) current,
        bool userInput = false) =>
        previous.Offset != current.Offset &&
        (userInput || (previous.Extent == current.Extent && previous.Viewport == current.Viewport));

    public static bool FollowTail(bool following, bool scrolled, double distance) => scrolled ? IsNearEdge(distance) : following;

    public static (bool Load, bool Armed) CheckHistoryEdge(
        bool armed, bool scrolled, double distance, double extent, double viewport) =>
        IsNearEdge(distance)
            ? (armed || scrolled || extent <= viewport, false)
            : (false, true);

    public static double CenterOffset(double offset, double top, double height, double viewport, double extent) =>
        Math.Clamp(offset + top - (viewport - height) / 2, 0, extent);
}
