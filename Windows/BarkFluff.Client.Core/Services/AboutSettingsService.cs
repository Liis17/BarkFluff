using WebApiClient = BarkFluff.WebApi.Core.WebApi;

namespace BarkFluff.Client.Core.Services;

public sealed class AboutSettingsService(WebApiClient webApi, IClientSession session) : SessionScopedService(webApi, session), IAboutSettingsService
{
    public async Task<bool> PingBeaconAsync(CancellationToken cancellationToken = default) =>
        (await WebApi.GetServerInfo(Parameters)).error.IsSuccess;
}
