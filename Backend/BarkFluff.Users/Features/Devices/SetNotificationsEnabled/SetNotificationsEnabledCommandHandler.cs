using BarkFluff.GrpcServer.Tracker;
using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Users.Persistence.Services;

using MediatR;

namespace BarkFluff.Users.Features.Devices.SetNotificationsEnabled;

public class SetNotificationsEnabledCommandHandler(
    DevicesStorage devicesStorage,
    UserContext userContext,
    RequestContext requestContext,
    ILogger<SetNotificationsEnabledCommandHandler> logger)
    : IRequestHandler<SetNotificationsEnabledCommand, Unit>
{
    public async Task<Unit> Handle(SetNotificationsEnabledCommand request, CancellationToken cancellationToken)
    {
        logger.LogDebug(
            "Изменение статуса уведомлений для пользователя {UserId}, DeviceId: {DeviceId}, Enabled: {Enabled}",
            userContext.UserId, requestContext.DeviceId, request.Enabled);

        if (string.IsNullOrEmpty(requestContext.DeviceId) || !Guid.TryParse(requestContext.DeviceId, out var deviceGuid))
        {
            logger.LogWarning("Некорректный DeviceId: {DeviceId}", requestContext.DeviceId);
            return Unit.Value;
        }

        await devicesStorage.SetNotificationsEnabled(deviceGuid, userContext.UserId, request.Enabled);

        logger.LogInformation(
            "Уведомления {Status} для устройства {DeviceId} пользователя {UserId}",
            request.Enabled ? "включены" : "выключены", deviceGuid, userContext.UserId);

        return Unit.Value;
    }
}
