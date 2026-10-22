using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Users.Features.Prekeys;
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
        var deviceGuid = PrekeyRequestContext.RequireDeviceId(userContext);

        var signedPrekey = command.Request.SignedPrekey;
        var ecInput = signedPrekey == null
            ? ((long PrekeyId, byte[] PublicKey, byte[] Signature)?)null
            : (signedPrekey.PrekeyId, signedPrekey.PublicKey.ToByteArray(), signedPrekey.Signature.ToByteArray());
        var kyberInput = command.Request.LastResortKyberPrekey == null
            ? null
            : new KyberPrekeyInput(
                command.Request.LastResortKyberPrekey.PrekeyId,
                command.Request.LastResortKyberPrekey.PublicKey.ToByteArray(),
                command.Request.LastResortKyberPrekey.Signature.ToByteArray());

        await prekeyStorage.RotateSignedPrekeyAsync(
            deviceGuid,
            userContext.UserId,
            ecInput,
            kyberInput,
            cancellationToken);

        logger.LogInformation(
            "Обновлены signed prekeys устройства {DeviceId} (user {UserId})",
            deviceGuid, userContext.UserId);

        return Unit.Value;
    }
}
