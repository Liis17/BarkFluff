using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Proto.Users;
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
        if (string.IsNullOrEmpty(userContext.DeviceId) || !Guid.TryParse(userContext.DeviceId, out var deviceGuid))
        {
            logger.LogWarning("ReplenishOneTimePrekeys: некорректный DeviceId {DeviceId}", userContext.DeviceId);
            return new ReplenishOneTimePrekeysResponse();
        }

        var prekeys = command.Request.Prekeys
            .Select(p => ((long)p.PrekeyId, p.PublicKey.ToByteArray()))
            .ToList();

        var total = await prekeyStorage.ReplenishOneTimePrekeysAsync(
            deviceGuid,
            userContext.UserId,
            prekeys);

        logger.LogInformation(
            "Пополнение one-time prekeys устройства {DeviceId} (user {UserId}): добавлено {Added}, всего {Total}",
            deviceGuid, userContext.UserId, prekeys.Count, total);

        return new ReplenishOneTimePrekeysResponse
        {
            TotalOneTimePrekeys = total,
        };
    }
}
