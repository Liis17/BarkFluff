using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Users.Features.Prekeys;
using BarkFluff.Proto.Users;
using BarkFluff.Users.Persistence.Services;

using MediatR;

namespace BarkFluff.Users.Features.Prekeys.RegisterPrekeyBundle;

public class RegisterPrekeyBundleCommandHandler(
    PrekeyStorage prekeyStorage,
    UserContext userContext,
    ILogger<RegisterPrekeyBundleCommandHandler> logger)
    : IRequestHandler<RegisterPrekeyBundleCommand, Unit>
{
    public async Task<Unit> Handle(RegisterPrekeyBundleCommand command, CancellationToken cancellationToken)
    {
        var deviceGuid = PrekeyRequestContext.RequireDeviceId(userContext);

        var request = command.Request;

        var signedPrekey = request.SignedPrekey
            ?? throw new PrekeyException(Grpc.Core.StatusCode.InvalidArgument, "SignedPrekey обязателен");

        var oneTimePrekeys = request.OneTimePrekeys
            .Select(p => ((long)p.PrekeyId, p.PublicKey.ToByteArray()))
            .ToList();
        var oneTimeKyberPrekeys = request.OneTimeKyberPrekeys
            .Select(ToKyberInput)
            .ToList();
        var lastResortKyberPrekey = request.LastResortKyberPrekey == null
            ? null
            : ToKyberInput(request.LastResortKyberPrekey);

        await prekeyStorage.RegisterBundleAsync(
            deviceGuid,
            userContext.UserId,
            request.RegistrationId,
            request.IdentityPubkey.ToByteArray(),
            signedPrekey.PrekeyId,
            signedPrekey.PublicKey.ToByteArray(),
            signedPrekey.Signature.ToByteArray(),
            oneTimePrekeys,
            lastResortKyberPrekey,
            oneTimeKyberPrekeys,
            cancellationToken);

        logger.LogInformation(
            "Зарегистрирован prekey-bundle устройства {DeviceId} пользователя {UserId}, one-time prekeys: {Count}",
            deviceGuid, userContext.UserId, oneTimePrekeys.Count);

        return Unit.Value;
    }

    private static KyberPrekeyInput ToKyberInput(SignedKyberPreKey prekey) =>
        new(prekey.PrekeyId, prekey.PublicKey.ToByteArray(), prekey.Signature.ToByteArray());
}
