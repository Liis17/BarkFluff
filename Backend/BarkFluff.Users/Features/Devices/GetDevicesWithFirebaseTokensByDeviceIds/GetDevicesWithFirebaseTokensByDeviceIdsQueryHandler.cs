using BarkFluff.Proto.Users;
using BarkFluff.Users.Persistence.Services;

using MediatR;

namespace BarkFluff.Users.Features.Devices.GetDevicesWithFirebaseTokensByDeviceIds;

public class GetDevicesWithFirebaseTokensByDeviceIdsQueryHandler(
    DevicesStorage devicesStorage,
    ILogger<GetDevicesWithFirebaseTokensByDeviceIdsQueryHandler> logger)
    : IRequestHandler<GetDevicesWithFirebaseTokensByDeviceIdsQuery, GetDevicesWithFirebaseTokensResponse>
{
    public async Task<GetDevicesWithFirebaseTokensResponse> Handle(GetDevicesWithFirebaseTokensByDeviceIdsQuery request, CancellationToken cancellationToken)
    {
        logger.LogDebug(
            "Получение Firebase токенов для {Count} устройств",
            request.DeviceIds.Count);

        var tokens = await devicesStorage.GetDevicesWithFirebaseTokensByDeviceIds(request.DeviceIds);

        var response = new GetDevicesWithFirebaseTokensResponse();
        foreach (var (userId, deviceId, firebaseToken) in tokens)
        {
            response.Tokens.Add(new DeviceFirebaseToken
            {
                UserId = userId,
                DeviceId = deviceId,
                FirebaseToken = firebaseToken
            });
        }

        logger.LogDebug(
            "Найдено {Count} устройств с Firebase токенами",
            response.Tokens.Count);

        return response;
    }
}
