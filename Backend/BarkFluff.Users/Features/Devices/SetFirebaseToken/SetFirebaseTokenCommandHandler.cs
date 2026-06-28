using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Users.Persistence.Services;

using MediatR;

namespace BarkFluff.Users.Features.Devices.SetFirebaseToken;

public class SetFirebaseTokenCommandHandler(
    DevicesStorage devicesStorage,
    UserContext userContext,
    ILogger<SetFirebaseTokenCommandHandler> logger)
    : IRequestHandler<SetFirebaseTokenCommand, Unit>
{
    public async Task<Unit> Handle(SetFirebaseTokenCommand request, CancellationToken cancellationToken)
    {
        logger.LogDebug(
            "Установка Firebase токена для пользователя {UserId}, DeviceId: {DeviceId}",
            userContext.UserId, userContext.DeviceId);

        if (string.IsNullOrEmpty(userContext.DeviceId) || !Guid.TryParse(userContext.DeviceId, out var deviceGuid))
        {
            logger.LogWarning("Некорректный DeviceId: {DeviceId}", userContext.DeviceId);
            return Unit.Value;
        }

        if (string.IsNullOrWhiteSpace(request.FirebaseToken) || request.FirebaseToken.Length > 256)
        {
            logger.LogWarning("Некорректный Firebase токен длиной {Length}", request.FirebaseToken?.Length ?? 0);
            return Unit.Value;
        }

        await devicesStorage.SetFirebaseToken(deviceGuid, userContext.UserId, request.FirebaseToken);

        logger.LogInformation(
            "Firebase токен установлен для устройства {DeviceId} пользователя {UserId}",
            deviceGuid, userContext.UserId);

        return Unit.Value;
    }
}
