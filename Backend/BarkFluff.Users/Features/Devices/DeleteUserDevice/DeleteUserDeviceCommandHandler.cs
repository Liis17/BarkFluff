using BarkFluff.Proto.Users;
using BarkFluff.Users.Persistence.Services;

using MediatR;

namespace BarkFluff.Users.Features.Devices.DeleteUserDevice;

public class DeleteUserDeviceCommandHandler(
    DevicesStorage devicesStorage,
    ILogger<DeleteUserDeviceCommandHandler> logger)
    : IRequestHandler<DeleteUserDeviceCommand, DeleteUserDeviceResponse>
{
    public async Task<DeleteUserDeviceResponse> Handle(DeleteUserDeviceCommand request, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Удаление устройства {DeviceId} для пользователя {UserId}",
            request.DeviceId, request.UserId);

        await devicesStorage.DeleteDevice(request.DeviceId, request.UserId);

        logger.LogInformation(
            "Устройство {DeviceId} успешно удалено для пользователя {UserId}",
            request.DeviceId, request.UserId);

        return new DeleteUserDeviceResponse();
    }
}
