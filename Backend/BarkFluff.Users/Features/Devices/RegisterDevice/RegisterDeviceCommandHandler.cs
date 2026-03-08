using BarkFluff.Proto.Users;
using BarkFluff.Users.Persistence.Services;

using Google.Protobuf.WellKnownTypes;

using MediatR;

namespace BarkFluff.Users.Features.Devices.RegisterDevice;

public class RegisterDeviceCommandHandler(
    DevicesStorage devicesStorage,
    ILogger<RegisterDeviceCommandHandler> logger)
    : IRequestHandler<RegisterDeviceCommand, RegisterDeviceResponse>
{
    public async Task<RegisterDeviceResponse> Handle(RegisterDeviceCommand request, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Регистрация устройства {DeviceId} для пользователя {UserId}",
            request.DeviceId, request.UserId);

        var device = await devicesStorage.RegisterOrUpdateDevice(
            request.DeviceId,
            request.UserId,
            request.OriginalName,
            request.AppName,
            request.OperationSystem,
            request.Location);

        logger.LogInformation(
            "Устройство {DeviceId} успешно зарегистрировано для пользователя {UserId}",
            request.DeviceId, request.UserId);

        return new RegisterDeviceResponse
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
