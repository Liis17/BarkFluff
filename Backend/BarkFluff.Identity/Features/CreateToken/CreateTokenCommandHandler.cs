using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Identity.Persistence.Services;
using BarkFluff.Identity.Services;
using BarkFluff.Proto.Identity;
using BarkFluff.Proto.Users;
using BarkFluff.Shared.Exceptions.Identity;

using MediatR;

namespace BarkFluff.Identity.Features.CreateToken;

public class CreateTokenCommandHandler(RefreshTokensStorage refreshTokensStorage, JwtService jwtService,
    UsersServerApi.UsersServerApiClient usersClient,
    MetricsCollector metrics, ILogger<CreateTokenCommandHandler> logger)
    : IRequestHandler<CreateTokenCommand, CreateTokenResponse>
{
    public async Task<CreateTokenResponse> Handle(CreateTokenCommand request, CancellationToken cancellationToken)
    {
        logger.LogDebug("Запрос на обновление токена");

        var accessToken = await refreshTokensStorage.FindRefreshToken(request.RefreshToken);

        if (accessToken == null
            || accessToken.ExpiresAt < DateTime.UtcNow
            || string.IsNullOrEmpty(accessToken.DeviceId))
        {
            metrics.Increment("tokens_refresh_invalid");
            logger.LogWarning(
                "Невалидный refresh token. Token найден: {TokenFound}, Истек: {IsExpired}",
                accessToken != null,
                accessToken?.ExpiresAt < DateTime.UtcNow
            );
            throw new InvalidRefreshTokenException();
        }

        logger.LogDebug("Генерация access token для пользователя {UserId}", accessToken.UserId);

        var token = jwtService.GenerateUserToken(accessToken.UserId, accessToken.DeviceId);

        metrics.Increment("tokens_refreshed");

        logger.LogInformation(
            "Access token успешно обновлен для пользователя {UserId}, устройство: {DeviceId}",
            accessToken.UserId,
            accessToken.DeviceId
        );

        // Обновляем имя устройства и версию приложения в Users при refresh токена.
        // Поля заполнены только для настоящего внешнего refresh (IdentityApiService.CreateToken),
        // внутренние вызовы (логин, серверная сессия) их не передают.
        if (!string.IsNullOrEmpty(request.DeviceName)
            && !string.IsNullOrEmpty(request.AppName)
            && !string.IsNullOrEmpty(request.AppVersion))
        {
            try
            {
                await usersClient.UpdateDeviceAppInfoAsync(new UpdateDeviceAppInfoRequest
                {
                    DeviceId = accessToken.DeviceId,
                    UserId = accessToken.UserId,
                    OriginalName = request.DeviceName,
                    AppName = $"{request.AppName} v.{request.AppVersion}",
                });
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Не удалось обновить данные устройства {DeviceId} при refresh токена",
                    accessToken.DeviceId);
            }
        }

        return new CreateTokenResponse { AccessToken = token, };
    }
}