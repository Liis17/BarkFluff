using BarkFluff.Proto.Users;
using BarkFluff.Users.Persistence.Services;

using MediatR;

namespace BarkFluff.Users.Features.Devices.UpdateDeviceAppInfo;

public class UpdateDeviceAppInfoCommandHandler(
    DevicesStorage devicesStorage,
    ILogger<UpdateDeviceAppInfoCommandHandler> logger)
    : IRequestHandler<UpdateDeviceAppInfoCommand, UpdateDeviceAppInfoResponse>
{
    public async Task<UpdateDeviceAppInfoResponse> Handle(UpdateDeviceAppInfoCommand request, CancellationToken cancellationToken)
    {
        var updated = await devicesStorage.UpdateDeviceAppInfoIfChanged(
            request.DeviceId,
            request.UserId,
            request.OriginalName,
            request.AppName);

        if (updated)
        {
            logger.LogInformation(
                "Обновлены имя устройства/версия приложения для устройства {DeviceId} пользователя {UserId}",
                request.DeviceId, request.UserId);
        }

        return new UpdateDeviceAppInfoResponse { Updated = updated };
    }
}
