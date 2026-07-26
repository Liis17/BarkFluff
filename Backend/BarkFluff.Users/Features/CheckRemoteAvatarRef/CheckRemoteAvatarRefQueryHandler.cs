using BarkFluff.Proto.Users;
using BarkFluff.Users.Persistence.Services;

using MediatR;

namespace BarkFluff.Users.Features.CheckRemoteAvatarRef;

/// <summary>
/// Anti-open-proxy для публичного маршрута аватаров (этап 3.4): пара (нода, file_id) должна
/// реально фигурировать в кеше remote-профилей.
/// </summary>
/// <remarks>
/// Без этой проверки <c>/download/fed/{server}/{fileId}</c> проксировал бы любой файл с любой
/// известной ноды по произвольному Guid — то есть работал бы открытым прокси.
/// </remarks>
public class CheckRemoteAvatarRefQueryHandler
    : IRequestHandler<CheckRemoteAvatarRefQuery, CheckRemoteAvatarRefResponse>
{
    private readonly RemoteUsersStorage _remoteUsersStorage;

    public CheckRemoteAvatarRefQueryHandler(RemoteUsersStorage remoteUsersStorage)
    {
        _remoteUsersStorage = remoteUsersStorage;
    }

    public async Task<CheckRemoteAvatarRefResponse> Handle(
        CheckRemoteAvatarRefQuery request,
        CancellationToken cancellationToken)
    {
        var serverName = request.ServerName.Trim().ToLowerInvariant();

        if (serverName.Length == 0 || string.IsNullOrWhiteSpace(request.FileId))
        {
            return new CheckRemoteAvatarRefResponse { Exists = false };
        }

        var exists = await _remoteUsersStorage.HasAvatarRefAsync(serverName, request.FileId);

        return new CheckRemoteAvatarRefResponse { Exists = exists };
    }
}
