using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json.Serialization;

namespace BarkFluff.Client.Core.Services;

public sealed class UpdateService(HttpClient httpClient) : IUpdateService
{
    private const string BaseStorageUrl = "https://storage.barkfluff.com/get/barkfluffwindows";

    public async Task<UpdateCheckResult> CheckForUpdatesAsync(UpdateChannel channel, CancellationToken cancellationToken = default)
    {
        var channelName = channel == UpdateChannel.Release ? "release" : "beta";
        var response = await httpClient.GetFromJsonAsync<VersionResponse>($"{BaseStorageUrl}/{channelName}/version", cancellationToken);
        var latestVersion = response?.Version;
        var currentVersion = Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0);
        var hasUpdate = Version.TryParse(latestVersion, out var remoteVersion) && remoteVersion > currentVersion;

        return new UpdateCheckResult(latestVersion, $"{BaseStorageUrl}/{channelName}/", hasUpdate);
    }

    private sealed record VersionResponse([property: JsonPropertyName("version")] string? Version);
}
