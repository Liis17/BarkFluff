using BarkFluff.GrpcServer.Tracker;
using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Users.Persistence.Services;
using MediatR;
using Microsoft.Extensions.Logging;

namespace BarkFluff.Users.Features.Devices.SetFirebaseToken;

public class SetFirebaseTokenCommandHandler(
    DevicesStorage devicesStorage,
    UserContext userContext,
    RequestContext requestContext,
    ILogger<SetFirebaseTokenCommandHandler> logger)
    : IRequestHandler<SetFirebaseTokenCommand, Unit>
{
    public async Task<Unit> Handle(SetFirebaseTokenCommand request, CancellationToken cancellationToken)
    {
        logger.LogDebug(
            "Установка Firebase токена для пользователя {UserId}, DeviceId: {DeviceId}",
            userContext.UserId, requestContext.DeviceId);

        if (string.IsNullOrEmpty(requestContext.DeviceId) || !Guid.TryParse(requestContext.DeviceId, out var deviceGuid))
        {
            logger.LogWarning("Некорректный DeviceId: {DeviceId}", requestContext.DeviceId);
            return Unit.Value;
        }

        await devicesStorage.SetFirebaseToken(deviceGuid, userContext.UserId, request.FirebaseToken);

        logger.LogInformation(
            "Firebase токен установлен для устройства {DeviceId} пользователя {UserId}",
            deviceGuid, userContext.UserId);

        return Unit.Value;
    }
}
