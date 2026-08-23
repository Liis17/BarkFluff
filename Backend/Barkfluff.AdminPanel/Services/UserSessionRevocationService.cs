using BarkFluff.Proto.Identity;

namespace Barkfluff.AdminPanel.Services;

public sealed record RevokeAllUserSessionsResult(
    int RequestedCount,
    int RevokedCount,
    IReadOnlyList<string> FailedDeviceIds);

public class UserSessionRevocationService(IdentityServerApi.IdentityServerApiClient identityClient)
{
    public async Task<RevokeAllUserSessionsResult> RevokeAllAsync(
        long userId,
        CancellationToken cancellationToken = default)
    {
        var response = await identityClient.GetActiveSessionsServerAsync(
            new GetActiveSessionsServerRequest { UserId = userId },
            cancellationToken: cancellationToken);

        var deviceIds = response.Sessions
            .Select(session => session.DeviceId)
            .Where(deviceId => !string.IsNullOrWhiteSpace(deviceId))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var results = await Task.WhenAll(deviceIds.Select(deviceId =>
            RevokeAsync(userId, deviceId, cancellationToken)));
        var failed = results
            .Where(result => !result.revoked)
            .Select(result => result.deviceId)
            .ToList();

        return new RevokeAllUserSessionsResult(
            deviceIds.Count,
            deviceIds.Count - failed.Count,
            failed);
    }

    private async Task<(string deviceId, bool revoked)> RevokeAsync(
        long userId,
        string deviceId,
        CancellationToken cancellationToken)
    {
        try
        {
            await identityClient.RemoveActiveSessionServerAsync(
                new RemoveActiveSessionServerRequest
                {
                    UserId = userId,
                    DeviceId = deviceId
                },
                cancellationToken: cancellationToken);
            return (deviceId, true);
        }
        catch when (!cancellationToken.IsCancellationRequested)
        {
            return (deviceId, false);
        }
    }
}
