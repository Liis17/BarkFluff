using BarkFluff.Proto.Users;
using BarkFluff.Users.Persistence.Services;

using Google.Protobuf.WellKnownTypes;

using MediatR;

namespace BarkFluff.Users.Features.Devices.GetUserDevices;

public class GetUserDevicesQueryHandler(
    DevicesStorage devicesStorage,
    ILogger<GetUserDevicesQueryHandler> logger)
    : IRequestHandler<GetUserDevicesQuery, GetUserDevicesResponse>
{
    public async Task<GetUserDevicesResponse> Handle(GetUserDevicesQuery request, CancellationToken cancellationToken)
    {
        logger.LogDebug("Получение устройств для пользователя {UserId}", request.UserId);

        var devices = await devicesStorage.GetDevicesByUserId(request.UserId);

        var response = new GetUserDevicesResponse();
        response.Devices.AddRange(devices.Select(d => new Device
        {
            DeviceId = d.Id.ToString(),
            UserId = d.UserId,
            OriginalName = d.OriginalName,
            CustomName = d.CustomName ?? "",
            AuthorizedAt = Timestamp.FromDateTime(d.AuthorizedAt),
            AppName = d.AppName ?? "",
            OperationSystem = d.OperationSystem ?? "",
            Location = d.Location ?? ""
        }));

        return response;
    }
}
