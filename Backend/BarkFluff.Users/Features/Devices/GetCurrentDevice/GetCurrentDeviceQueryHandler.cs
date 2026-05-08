using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Proto.Users;
using BarkFluff.Users.Persistence.Services;

using Google.Protobuf.WellKnownTypes;

using MediatR;

namespace BarkFluff.Users.Features.Devices.GetCurrentDevice;

public class GetCurrentDeviceQueryHandler(
    DevicesStorage devicesStorage,
    UserContext userContext,
    ILogger<GetCurrentDeviceQueryHandler> logger)
    : IRequestHandler<GetCurrentDeviceQuery, GetCurrentDeviceResponse>
{
    public async Task<GetCurrentDeviceResponse> Handle(GetCurrentDeviceQuery request, CancellationToken cancellationToken)
    {
        logger.LogDebug(
            "Получение текущего устройства для пользователя {UserId}, DeviceId: {DeviceId}",
            userContext.UserId, userContext.DeviceId);

        if (string.IsNullOrEmpty(userContext.DeviceId) || !Guid.TryParse(userContext.DeviceId, out var deviceGuid))
        {
            return new GetCurrentDeviceResponse();
        }

        var device = await devicesStorage.GetDeviceById(deviceGuid, userContext.UserId);

        if (device == null)
        {
            return new GetCurrentDeviceResponse();
        }

        return new GetCurrentDeviceResponse
        {
            Device = new Device
            {
                DeviceId = device.Id.ToString(),
                UserId = device.UserId,
                OriginalName = device.OriginalName,
                CustomName = device.CustomName ?? "",
                AuthorizedAt = Timestamp.FromDateTime(device.AuthorizedAt),
                AppName = device.AppName ?? "",
                OperationSystem = device.OperationSystem ?? "",
                Location = device.Location ?? "",
                NotificationsEnabled = device.NotificationsEnabled
            }
        };
    }
}
