using BarkFluff.Proto.Users;
using BarkFluff.Users.Persistence.Services;

using MediatR;

namespace BarkFluff.Users.Features.Devices.GetAllDevicesWithFirebaseTokens;

public class GetAllDevicesWithFirebaseTokensQueryHandler(
    DevicesStorage devicesStorage,
    ILogger<GetAllDevicesWithFirebaseTokensQueryHandler> logger)
    : IRequestHandler<GetAllDevicesWithFirebaseTokensQuery, GetDevicesWithFirebaseTokensResponse>
{
    public async Task<GetDevicesWithFirebaseTokensResponse> Handle(GetAllDevicesWithFirebaseTokensQuery request, CancellationToken cancellationToken)
    {
        logger.LogDebug("Получение Firebase токенов для ВСЕХ устройств");

        var tokens = await devicesStorage.GetAllDevicesWithFirebaseTokens();

        var response = new GetDevicesWithFirebaseTokensResponse();
        foreach (var (userId, deviceId, firebaseToken, pushPlatform) in tokens)
        {
            response.Tokens.Add(new DeviceFirebaseToken
            {
                UserId = userId,
                DeviceId = deviceId,
                FirebaseToken = firebaseToken,
                PushPlatform = pushPlatform == BarkFluff.Users.Domain.DevicePushPlatform.Web
                    ? PushPlatform.Web
                    : PushPlatform.Android
            });
        }

        logger.LogInformation(
            "Найдено {Count} устройств с Firebase токенами (broadcast)",
            response.Tokens.Count);

        return response;
    }
}
