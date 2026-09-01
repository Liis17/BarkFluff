namespace BarkFluff.Client.Core.Services;

internal static class UpdateChannelResolver
{
    public const string ReleaseIdentity = "7895OrbitinSpace.Barkfluff";
    public const string DevIdentity = "7895OrbitinSpace.Barkfluff.Dev";
    public const string NightlyIdentity = "7895OrbitinSpace.Barkfluff.Nightly";

    public static UpdateChannel? Resolve(string? identityName) => identityName switch
    {
        ReleaseIdentity => UpdateChannel.Release,
        DevIdentity => UpdateChannel.Dev,
        NightlyIdentity => UpdateChannel.Nightly,
        _ => null
    };

    public static string GetRouteSegment(UpdateChannel channel) => channel switch
    {
        UpdateChannel.Release => "release",
        UpdateChannel.Dev => "dev",
        UpdateChannel.Nightly => "nightly",
        _ => throw new ArgumentOutOfRangeException(nameof(channel), channel, null)
    };
}
