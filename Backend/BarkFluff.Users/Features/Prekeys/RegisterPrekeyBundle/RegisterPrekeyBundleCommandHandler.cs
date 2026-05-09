using BarkFluff.GrpcServer.XAuth;
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
        if (string.IsNullOrEmpty(userContext.DeviceId) || !Guid.TryParse(userContext.DeviceId, out var deviceGuid))
        {
            logger.LogWarning("RegisterPrekeyBundle: некорректный DeviceId {DeviceId}", userContext.DeviceId);
            return Unit.Value;
        }

        var request = command.Request;

        if (request.SignedPrekey == null)
        {
            throw new InvalidOperationException("SignedPrekey обязателен");
        }

        var oneTimePrekeys = request.OneTimePrekeys
            .Select(p => ((long)p.PrekeyId, p.PublicKey.ToByteArray()))
            .ToList();

        await prekeyStorage.RegisterBundleAsync(
            deviceGuid,
            userContext.UserId,
            request.RegistrationId,
            request.IdentityPubkey.ToByteArray(),
            request.SignedPrekey.PrekeyId,
            request.SignedPrekey.PublicKey.ToByteArray(),
            request.SignedPrekey.Signature.ToByteArray(),
            oneTimePrekeys);

        logger.LogInformation(
            "Зарегистрирован prekey-bundle устройства {DeviceId} пользователя {UserId}, one-time prekeys: {Count}",
            deviceGuid, userContext.UserId, oneTimePrekeys.Count);

        return Unit.Value;
    }
}
