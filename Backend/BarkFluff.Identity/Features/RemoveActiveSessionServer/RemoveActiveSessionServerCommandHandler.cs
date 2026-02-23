using BarkFluff.Identity.Persistence.Exceptions;
using BarkFluff.Identity.Persistence.Services;
using BarkFluff.Proto.Identity;
using BarkFluff.Proto.Users;
using BarkFluff.Shared.Exceptions.Identity;
using MediatR;
using Microsoft.Extensions.Logging;

namespace BarkFluff.Identity.Features.RemoveActiveSessionServer;

public class RemoveActiveSessionServerCommandHandler : IRequestHandler<RemoveActiveSessionServerCommand, RemoveActiveSessionResponse>
{
    private readonly RefreshTokensStorage _refreshTokensStorage;
    private readonly UsersServerApi.UsersServerApiClient _usersClient;
    private readonly ILogger<RemoveActiveSessionServerCommandHandler> _logger;

    public RemoveActiveSessionServerCommandHandler(RefreshTokensStorage refreshTokensStorage,
        UsersServerApi.UsersServerApiClient usersClient,
        ILogger<RemoveActiveSessionServerCommandHandler> logger)
    {
        _refreshTokensStorage = refreshTokensStorage;
        _usersClient = usersClient;
        _logger = logger;
    }

    public async Task<RemoveActiveSessionResponse> Handle(RemoveActiveSessionServerCommand request, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Удаление сессии по DeviceId {DeviceId} для пользователя {UserId} (server)",
            request.DeviceId, request.UserId);

        try
        {
            await _refreshTokensStorage.DeleteRefreshTokensByDeviceId(request.DeviceId, request.UserId);
        }
        catch (RefreshTokenNotFoundException ex)
        {
            _logger.LogWarning(ex,
                "Сессия для устройства {DeviceId} не найдена для пользователя {UserId}",
                request.DeviceId, request.UserId);
            throw new SessionNotFoundException();
        }

        try
        {
            await _usersClient.DeleteUserDeviceAsync(new DeleteUserDeviceRequest
            {
                DeviceId = request.DeviceId,
                UserId = request.UserId
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Не удалось удалить устройство {DeviceId} из Users сервиса для пользователя {UserId}",
                request.DeviceId, request.UserId);
        }

        return new RemoveActiveSessionResponse();
    }
}
