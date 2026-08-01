using BarkFluff.Proto.Users;
using BarkFluff.Users.Persistence.Services;

using MediatR;

namespace BarkFluff.Users.Features.Devices.GetDevicesWithFirebaseTokens;

public class GetDevicesWithFirebaseTokensQueryHandler(
    DevicesStorage devicesStorage,
    ILogger<GetDevicesWithFirebaseTokensQueryHandler> logger)
    : IRequestHandler<GetDevicesWithFirebaseTokensQuery, GetDevicesWithFirebaseTokensResponse>
{
    public async Task<GetDevicesWithFirebaseTokensResponse> Handle(GetDevicesWithFirebaseTokensQuery request, CancellationToken cancellationToken)
    {
        logger.LogDebug(
            "Получение Firebase токенов для {Count} пользователей",
            request.UserIds.Count);

        var tokens = await devicesStorage.GetDevicesWithFirebaseTokens(request.UserIds, request.MutedChatFilter);

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

        logger.LogDebug(
            "Найдено {Count} устройств с Firebase токенами",
            response.Tokens.Count);

        return response;
    }
}
