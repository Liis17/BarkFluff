using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Identity.Persistence.Exceptions;
using BarkFluff.Identity.Persistence.Services;
using BarkFluff.Identity.Settings;
using BarkFluff.Proto.Identity;
using BarkFluff.Proto.Users;
using BarkFluff.Shared.Exceptions.Identity;
using BarkFluff.Shared.Queue.Identity;

using MassTransit;

using MediatR;

namespace BarkFluff.Identity.Features.RemoveActiveSession;

public class RemoveActiveSessionCommandHandler : IRequestHandler<RemoveActiveSessionCommand, RemoveActiveSessionResponse>
{
    private readonly RefreshTokensStorage _refreshTokensStorage;
    private readonly UserContext _userContext;
    private readonly UsersServerApi.UsersServerApiClient _usersClient;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly JwtSettings _jwtSettings;
    private readonly ILogger<RemoveActiveSessionCommandHandler> _logger;

    public RemoveActiveSessionCommandHandler(RefreshTokensStorage refreshTokensStorage, UserContext userContext,
        UsersServerApi.UsersServerApiClient usersClient, IPublishEndpoint publishEndpoint,
        JwtSettings jwtSettings,
        ILogger<RemoveActiveSessionCommandHandler> logger)
    {
        _refreshTokensStorage = refreshTokensStorage;
        _userContext = userContext;
        _usersClient = usersClient;
        _publishEndpoint = publishEndpoint;
        _jwtSettings = jwtSettings;
        _logger = logger;
    }

    public async Task<RemoveActiveSessionResponse> Handle(RemoveActiveSessionCommand request, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Попытка удаления сессии по DeviceId {DeviceId} для пользователя {UserId}",
            request.DeviceId,
            _userContext.UserId
        );

        try
        {
            await _refreshTokensStorage.DeleteRefreshTokensByDeviceId(request.DeviceId, _userContext.UserId);

            _logger.LogInformation(
                "Сессия для устройства {DeviceId} успешно удалена для пользователя {UserId}",
                request.DeviceId,
                _userContext.UserId
            );
        }
        catch (RefreshTokenNotFoundException ex)
        {
            _logger.LogWarning(
                ex,
                "Сессия для устройства {DeviceId} не найдена для пользователя {UserId}",
                request.DeviceId,
                _userContext.UserId
            );
            throw new SessionNotFoundException();
        }

        // Публикуем событие отзыва сессии для инвалидации access токенов
        await _publishEndpoint.Publish(new SessionRevokedEvent
        {
            UserId = _userContext.UserId,
            DeviceId = request.DeviceId,
            AccessTokenExpiresAt = DateTime.UtcNow.AddMinutes(_jwtSettings.ExpiryMinutes)
        });

        // Удаляем устройство из Users сервиса
        try
        {
            await _usersClient.DeleteUserDeviceAsync(new DeleteUserDeviceRequest
            {
                DeviceId = request.DeviceId,
                UserId = _userContext.UserId
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Не удалось удалить устройство {DeviceId} из Users сервиса для пользователя {UserId}",
                request.DeviceId, _userContext.UserId);
        }

        return new RemoveActiveSessionResponse();
    }
}
