using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Users.Persistence.Services;

using MediatR;

namespace BarkFluff.Users.Features.Devices.SetNotificationsEnabled;

public class SetNotificationsEnabledCommandHandler(
    DevicesStorage devicesStorage,
    UserContext userContext,
    ILogger<SetNotificationsEnabledCommandHandler> logger)
    : IRequestHandler<SetNotificationsEnabledCommand, Unit>
{
    public async Task<Unit> Handle(SetNotificationsEnabledCommand request, CancellationToken cancellationToken)
    {
        logger.LogDebug(
            "Изменение статуса уведомлений для пользователя {UserId}, DeviceId: {DeviceId}, Enabled: {Enabled}",
            userContext.UserId, userContext.DeviceId, request.Enabled);

        if (string.IsNullOrEmpty(userContext.DeviceId) || !Guid.TryParse(userContext.DeviceId, out var deviceGuid))
        {
            logger.LogWarning("Некорректный DeviceId: {DeviceId}", userContext.DeviceId);
            return Unit.Value;
        }

        await devicesStorage.SetNotificationsEnabled(deviceGuid, userContext.UserId, request.Enabled);

        logger.LogInformation(
            "Уведомления {Status} для устройства {DeviceId} пользователя {UserId}",
            request.Enabled ? "включены" : "выключены", deviceGuid, userContext.UserId);

        return Unit.Value;
    }
}
