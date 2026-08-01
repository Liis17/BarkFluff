using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Users.Persistence.Services;

using MediatR;

namespace BarkFluff.Users.Features.Devices.ClearFirebaseToken;

public class ClearFirebaseTokenCommandHandler(
    DevicesStorage devicesStorage,
    UserContext userContext,
    ILogger<ClearFirebaseTokenCommandHandler> logger)
    : IRequestHandler<ClearFirebaseTokenCommand, Unit>
{
    public async Task<Unit> Handle(ClearFirebaseTokenCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(userContext.DeviceId) || !Guid.TryParse(userContext.DeviceId, out var deviceGuid))
        {
            logger.LogWarning("Некорректный DeviceId: {DeviceId}", userContext.DeviceId);
            return Unit.Value;
        }

        await devicesStorage.ClearFirebaseToken(deviceGuid, userContext.UserId);
        logger.LogInformation("Firebase токен удалён для устройства {DeviceId} пользователя {UserId}", deviceGuid, userContext.UserId);
        return Unit.Value;
    }
}
