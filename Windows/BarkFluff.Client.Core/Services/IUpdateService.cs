namespace BarkFluff.Client.Core.Services;

public interface IUpdateService
{
    Task<UpdateCheckResult> CheckForUpdatesAsync(UpdateChannel channel, CancellationToken cancellationToken = default);
}

public enum UpdateChannel { Release, Beta }

public sealed record UpdateCheckResult(string? LatestVersion, string DownloadUrl, bool IsUpdateAvailable);
