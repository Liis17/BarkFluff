namespace BarkFluff.Client.Core.Services;

public interface IUpdateService
{
    UpdateChannel? CurrentChannel { get; }

    Task<UpdateCheckResult> CheckForUpdatesAsync(CancellationToken cancellationToken = default);

    Task<string?> DownloadUpdateAsync(CancellationToken cancellationToken = default);
}

public enum UpdateChannel
{
    Release,
    Dev,
    Nightly
}

public sealed record UpdateCheckResult(string? LatestVersion, bool IsUpdateAvailable);
