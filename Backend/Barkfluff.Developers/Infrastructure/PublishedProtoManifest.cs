namespace Barkfluff.Developers.Infrastructure;

internal static class PublishedProtoManifest
{
    public static IReadOnlyList<string> FileNames { get; } =
    [
        "shared.proto",
        "beacon_api.proto",
        "identity_api.proto",
        "users_api.proto",
        "messages_api.proto",
        "files_api.proto",
        "updates_api.proto",
        "onliner_api.proto",
        "fast_auth_api.proto",
        "navigator_api.proto"
    ];

    private static readonly HashSet<string> FileNameSet =
        new(FileNames, StringComparer.Ordinal);

    public static bool Contains(string? fileName)
    {
        return fileName is not null && FileNameSet.Contains(fileName);
    }
}
