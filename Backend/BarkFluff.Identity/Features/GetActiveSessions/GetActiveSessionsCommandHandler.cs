using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Identity.Persistence.Services;
using BarkFluff.Proto.Identity;
using BarkFluff.Proto.Users;
using Google.Protobuf.WellKnownTypes;
using MediatR;
using Microsoft.Extensions.Logging;

namespace BarkFluff.Identity.Features.GetActiveSessions;

public class GetActiveSessionsCommandHandler : IRequestHandler<GetActiveSessionsCommand, GetActiveSessionsResponse>
{
    private readonly RefreshTokensStorage _refreshTokensStorage;
    private readonly UserContext _userContext;
    private readonly UsersServerApi.UsersServerApiClient _usersClient;
    private readonly ILogger<GetActiveSessionsCommandHandler> _logger;

    public GetActiveSessionsCommandHandler(RefreshTokensStorage refreshTokensStorage, UserContext userContext,
        UsersServerApi.UsersServerApiClient usersClient,
        ILogger<GetActiveSessionsCommandHandler> logger)
    {
        _refreshTokensStorage = refreshTokensStorage;
        _userContext = userContext;
        _usersClient = usersClient;
        _logger = logger;
    }

    public async Task<GetActiveSessionsResponse> Handle(GetActiveSessionsCommand request, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Получение списка активных сессий для пользователя {UserId}", _userContext.UserId);

        var tokens = await _refreshTokensStorage.GetRefreshTokens(_userContext.UserId);

        _logger.LogInformation(
            "Получено {SessionCount} активных сессий для пользователя {UserId}",
            tokens.Count,
            _userContext.UserId
        );

        // Получаем информацию об устройствах из Users сервиса
        Dictionary<string, Device> devicesMap = new();
        try
        {
            var devicesResponse = await _usersClient.GetUserDevicesAsync(new GetUserDevicesRequest
            {
                UserId = _userContext.UserId
            });

            foreach (var device in devicesResponse.Devices)
            {
                devicesMap[device.DeviceId] = device;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Не удалось получить информацию об устройствах для пользователя {UserId}",
                _userContext.UserId);
        }

        return new GetActiveSessionsResponse()
        {
            Sessions =
            {
                tokens.Select(t =>
                {
                    var session = new GetActiveSessionsResponse.Types.Session()
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
