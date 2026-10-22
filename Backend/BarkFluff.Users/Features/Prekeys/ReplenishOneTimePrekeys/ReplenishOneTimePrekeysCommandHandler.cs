using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Proto.Users;
using BarkFluff.Users.Features.Prekeys;
using BarkFluff.Users.Persistence.Services;

using MediatR;

namespace BarkFluff.Users.Features.Prekeys.ReplenishOneTimePrekeys;

public class ReplenishOneTimePrekeysCommandHandler(
    PrekeyStorage prekeyStorage,
    UserContext userContext,
    ILogger<ReplenishOneTimePrekeysCommandHandler> logger)
    : IRequestHandler<ReplenishOneTimePrekeysCommand, ReplenishOneTimePrekeysResponse>
{
    public async Task<ReplenishOneTimePrekeysResponse> Handle(
        ReplenishOneTimePrekeysCommand command,
        CancellationToken cancellationToken)
    {
        var deviceGuid = PrekeyRequestContext.RequireDeviceId(userContext);

        var prekeys = command.Request.Prekeys
            .Select(p => ((long)p.PrekeyId, p.PublicKey.ToByteArray()))
            .ToList();
        var kyberPrekeys = command.Request.KyberPrekeys
            .Select(p => new KyberPrekeyInput(p.PrekeyId, p.PublicKey.ToByteArray(), p.Signature.ToByteArray()))
            .ToList();

        var totals = await prekeyStorage.ReplenishOneTimePrekeysAsync(
            deviceGuid,
            userContext.UserId,
            prekeys,
            kyberPrekeys,
            cancellationToken);

        logger.LogInformation(
            "Пополнение one-time prekeys устройства {DeviceId} (user {UserId}): EC {EcTotal}, Kyber {KyberTotal}",
            deviceGuid, userContext.UserId, totals.OneTimePrekeys, totals.OneTimeKyberPrekeys);

        return new ReplenishOneTimePrekeysResponse
        {
            TotalOneTimePrekeys = totals.OneTimePrekeys,
            TotalOneTimeKyberPrekeys = totals.OneTimeKyberPrekeys,
        };
    }
}
