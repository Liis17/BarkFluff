using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Proto.Users;
using BarkFluff.Users.Persistence.Services;

using MediatR;

namespace BarkFluff.Users.Features.Devices.RenameDevice;

public class RenameDeviceCommandHandler(
    DevicesStorage devicesStorage,
    UserContext userContext,
    ILogger<RenameDeviceCommandHandler> logger)
    : IRequestHandler<RenameDeviceCommand, RenameDeviceResponse>
{
    public async Task<RenameDeviceResponse> Handle(RenameDeviceCommand request, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Переименование устройства {DeviceId} для пользователя {UserId}",
            request.DeviceId, userContext.UserId);

        await devicesStorage.RenameDevice(request.DeviceId, userContext.UserId, request.CustomName);

        logger.LogInformation(
            "Устройство {DeviceId} успешно переименовано для пользователя {UserId}",
            request.DeviceId, userContext.UserId);

        return new RenameDeviceResponse();
    }
}
