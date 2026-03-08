using BarkFluff.Identity.Persistence.Services;
using BarkFluff.Proto.Identity;
using BarkFluff.Proto.Users;

using Google.Protobuf.WellKnownTypes;

using MediatR;

namespace BarkFluff.Identity.Features.GetActiveSessionsServer;

public class GetActiveSessionsServerCommandHandler : IRequestHandler<GetActiveSessionsServerCommand, GetActiveSessionsResponse>
{
    private readonly RefreshTokensStorage _refreshTokensStorage;
    private readonly UsersServerApi.UsersServerApiClient _usersClient;
    private readonly ILogger<GetActiveSessionsServerCommandHandler> _logger;

    public GetActiveSessionsServerCommandHandler(RefreshTokensStorage refreshTokensStorage,
        UsersServerApi.UsersServerApiClient usersClient,
        ILogger<GetActiveSessionsServerCommandHandler> logger)
    {
        _refreshTokensStorage = refreshTokensStorage;
        _usersClient = usersClient;
        _logger = logger;
    }

    public async Task<GetActiveSessionsResponse> Handle(GetActiveSessionsServerCommand request, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Получение списка активных сессий для пользователя {UserId} (server)", request.UserId);

        var tokens = await _refreshTokensStorage.GetRefreshTokens(request.UserId);

        Dictionary<string, Device> devicesMap = new();
        try
        {
            var devicesResponse = await _usersClient.GetUserDevicesAsync(new GetUserDevicesRequest
            {
                UserId = request.UserId
            });

            foreach (var device in devicesResponse.Devices)
            {
                devicesMap[device.DeviceId] = device;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Не удалось получить информацию об устройствах для пользователя {UserId}",
                request.UserId);
        }

        return new GetActiveSessionsResponse
        {
            Sessions =
            {
                tokens.Select(t =>
                {
                    var session = new GetActiveSessionsResponse.Types.Session
                    {
                        CreatedAt = Timestamp.FromDateTime(t.CreatedAt),
                        DeviceId = t.DeviceId ?? "",
                        ExpirationAt = Timestamp.FromDateTime(t.ExpiresAt),
                        Id = t.Id
                    };

                    if (!string.IsNullOrEmpty(t.DeviceId) && devicesMap.TryGetValue(t.DeviceId, out var device))
                    {
                        session.OriginalName = device.OriginalName;
                        session.CustomName = device.CustomName;
                        session.AppName = device.AppName;
                        session.OperationSystem = device.OperationSystem;
                        session.Location = device.Location;
                    }

                    return session;
                })
            }
        };
    }
}
