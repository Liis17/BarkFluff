using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Proto.Users;
using BarkFluff.Users.Persistence.Services;

using Google.Protobuf.WellKnownTypes;

using MediatR;

namespace BarkFluff.Users.Features.Devices.GetDevices;

public class GetDevicesQueryHandler(
    DevicesStorage devicesStorage,
    UserContext userContext,
    ILogger<GetDevicesQueryHandler> logger)
    : IRequestHandler<GetDevicesQuery, GetDevicesResponse>
{
    public async Task<GetDevicesResponse> Handle(GetDevicesQuery request, CancellationToken cancellationToken)
    {
        logger.LogDebug("Получение списка устройств для пользователя {UserId}", userContext.UserId);

        var devices = await devicesStorage.GetDevicesByUserId(userContext.UserId);

        var response = new GetDevicesResponse();
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
