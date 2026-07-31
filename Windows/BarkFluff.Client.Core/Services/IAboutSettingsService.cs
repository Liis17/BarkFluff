namespace BarkFluff.Client.Core.Services;

public interface IAboutSettingsService
{
    Task<bool> PingBeaconAsync(CancellationToken cancellationToken = default);
}
