using BarkFluff.GrpcServer.Metrics;
using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Identity.Persistence.Services;
using BarkFluff.Identity.Settings;
using BarkFluff.Proto.Identity;
using BarkFluff.Proto.Users;
using BarkFluff.Shared.Queue.Identity;

using MassTransit;

using MediatR;

namespace BarkFluff.Identity.Features.Logout;

public class LogoutCommandHandler(
    RefreshTokensStorage refreshTokensStorage,
    UserContext userContext,
    UsersServerApi.UsersServerApiClient usersClient,
    IPublishEndpoint publishEndpoint,
    JwtSettings jwtSettings,
    MetricsCollector metrics,
    ILogger<LogoutCommandHandler> logger) : IRequestHandler<LogoutCommand, LogoutResponse>
{
    public async Task<LogoutResponse> Handle(LogoutCommand request, CancellationToken cancellationToken)
    {
        var deviceId = userContext.DeviceId;
        var userId = userContext.UserId;

        logger.LogInformation(
            "Разлогин пользователя {UserId} с устройства {DeviceId}",
            userId, deviceId);

        await refreshTokensStorage.DeleteRefreshTokensByDeviceIdSafe(deviceId!, userId);

        await publishEndpoint.Publish(new SessionRevokedEvent
        {
            UserId = userId,
            DeviceId = deviceId!,
            AccessTokenExpiresAt = DateTime.UtcNow.AddMinutes(jwtSettings.ExpiryMinutes)
        }, cancellationToken);

        try
        {
            await usersClient.DeleteUserDeviceAsync(new DeleteUserDeviceRequest
            {
                DeviceId = deviceId,
                UserId = userId
            });
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Не удалось удалить устройство {DeviceId} из Users сервиса для пользователя {UserId}",
                deviceId, userId);
        }

        metrics.Increment("logouts");
        metrics.Increment("sessions_revoked");

        logger.LogInformation(
            "Пользователь {UserId} успешно разлогинен с устройства {DeviceId}",
            userId, deviceId);

        return new LogoutResponse();
    }
}
