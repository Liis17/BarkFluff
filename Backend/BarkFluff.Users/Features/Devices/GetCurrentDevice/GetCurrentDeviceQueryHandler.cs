using BarkFluff.GrpcServer.Tracker;
using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Proto.Users;
using BarkFluff.Users.Persistence.Services;
using Google.Protobuf.WellKnownTypes;
using MediatR;
using Microsoft.Extensions.Logging;

namespace BarkFluff.Users.Features.Devices.GetCurrentDevice;

public class GetCurrentDeviceQueryHandler(
    DevicesStorage devicesStorage,
    UserContext userContext,
    RequestContext requestContext,
    ILogger<GetCurrentDeviceQueryHandler> logger)
    : IRequestHandler<GetCurrentDeviceQuery, GetCurrentDeviceResponse>
{
    public async Task<GetCurrentDeviceResponse> Handle(GetCurrentDeviceQuery request, CancellationToken cancellationToken)
    {
        logger.LogDebug(
            "Получение текущего устройства для пользователя {UserId}, DeviceId: {DeviceId}",
            userContext.UserId, requestContext.DeviceId);

        if (string.IsNullOrEmpty(requestContext.DeviceId) || !Guid.TryParse(requestContext.DeviceId, out var deviceGuid))
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
                Location = device.Location ?? ""
            }
        };
    }
}
