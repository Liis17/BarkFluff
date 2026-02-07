using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Identity.Persistence.Exceptions;
using BarkFluff.Identity.Persistence.Services;
using BarkFluff.Proto.Identity;
using BarkFluff.Proto.Users;
using BarkFluff.Shared.Exceptions.Identity;
using MediatR;
using Microsoft.Extensions.Logging;

namespace BarkFluff.Identity.Features.RemoveActiveSession;

public class RemoveActiveSessionCommandHandler : IRequestHandler<RemoveActiveSessionCommand, RemoveActiveSessionResponse>
{
    private readonly RefreshTokensStorage _refreshTokensStorage;
    private readonly UserContext _userContext;
    private readonly UsersServerApi.UsersServerApiClient _usersClient;
    private readonly ILogger<RemoveActiveSessionCommandHandler> _logger;

    public RemoveActiveSessionCommandHandler(RefreshTokensStorage refreshTokensStorage, UserContext userContext,
        UsersServerApi.UsersServerApiClient usersClient,
        ILogger<RemoveActiveSessionCommandHandler> logger)
    {
        _refreshTokensStorage = refreshTokensStorage;
        _userContext = userContext;
        _usersClient = usersClient;
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
