using BarkFluff.Identity.Persistence.Services;
using BarkFluff.Identity.Services;
using BarkFluff.Proto.Identity;
using BarkFluff.Shared.Exceptions.Identity;
using MediatR;
using Microsoft.Extensions.Logging;

namespace BarkFluff.Identity.Features.CreateToken;

public class CreateTokenCommandHandler(RefreshTokensStorage refreshTokensStorage, JwtService jwtService,
    ILogger<CreateTokenCommandHandler> logger)
    : IRequestHandler<CreateTokenCommand, CreateTokenResponse>
{
    public async Task<CreateTokenResponse> Handle(CreateTokenCommand request, CancellationToken cancellationToken)
    {
        logger.LogDebug("Запрос на обновление токена");

        var accessToken = await refreshTokensStorage.FindRefreshToken(request.RefreshToken);

        if (accessToken == null
            || accessToken.ExpiresAt < DateTime.UtcNow
            || string.IsNullOrEmpty(accessToken.DeviceName))
        {
            logger.LogWarning(
                "Невалидный refresh token. Token найден: {TokenFound}, Истек: {IsExpired}",
                accessToken != null,
                accessToken?.ExpiresAt < DateTime.UtcNow
            );
            throw new InvalidRefreshTokenException();
        }

        logger.LogDebug("Генерация access token для пользователя {UserId}", accessToken.UserId);

        var token = jwtService.GenerateUserToken(accessToken.UserId);

        logger.LogInformation(
            "Access token успешно обновлен для пользователя {UserId}, устройство: {DeviceName}",
            accessToken.UserId,
            accessToken.DeviceName
        );

        return new CreateTokenResponse { AccessToken = token, };
    }
}