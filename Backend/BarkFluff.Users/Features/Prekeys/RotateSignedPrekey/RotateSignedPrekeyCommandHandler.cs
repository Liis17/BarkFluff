using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Users.Persistence.Services;

using MediatR;

namespace BarkFluff.Users.Features.Prekeys.RotateSignedPrekey;

public class RotateSignedPrekeyCommandHandler(
    PrekeyStorage prekeyStorage,
    UserContext userContext,
    ILogger<RotateSignedPrekeyCommandHandler> logger)
    : IRequestHandler<RotateSignedPrekeyCommand, Unit>
{
    public async Task<Unit> Handle(RotateSignedPrekeyCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(userContext.DeviceId) || !Guid.TryParse(userContext.DeviceId, out var deviceGuid))
        {
            logger.LogWarning("RotateSignedPrekey: некорректный DeviceId {DeviceId}", userContext.DeviceId);
            return Unit.Value;
        }

        var signedPrekey = command.Request.SignedPrekey
            ?? throw new InvalidOperationException("SignedPrekey обязателен");

        await prekeyStorage.RotateSignedPrekeyAsync(
            deviceGuid,
            userContext.UserId,
            signedPrekey.PrekeyId,
            signedPrekey.PublicKey.ToByteArray(),
            signedPrekey.Signature.ToByteArray());

        logger.LogInformation(
            "Сменён signed prekey устройства {DeviceId} (user {UserId}), новый id {PrekeyId}",
            deviceGuid, userContext.UserId, signedPrekey.PrekeyId);

        return Unit.Value;
    }
}
