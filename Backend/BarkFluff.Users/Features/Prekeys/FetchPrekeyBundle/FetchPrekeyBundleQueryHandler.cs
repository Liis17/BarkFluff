using BarkFluff.Proto.Users;
using BarkFluff.Users.Mapping;
using BarkFluff.Users.Persistence.Services;

using MediatR;

namespace BarkFluff.Users.Features.Prekeys.FetchPrekeyBundle;

public class FetchPrekeyBundleQueryHandler(
    PrekeyStorage prekeyStorage,
    ILogger<FetchPrekeyBundleQueryHandler> logger)
    : IRequestHandler<FetchPrekeyBundleQuery, FetchPrekeyBundleResponse>
{
    public async Task<FetchPrekeyBundleResponse> Handle(FetchPrekeyBundleQuery query, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(query.DeviceId, out var deviceGuid))
        {
            throw new InvalidOperationException("Некорректный DeviceId");
        }

        var result = await prekeyStorage.FetchBundleAsync(query.UserId, deviceGuid);

        if (result == null)
        {
            throw new InvalidOperationException("Bundle устройства не найден");
        }

        var (bundle, prekey, remaining) = result.Value;

        if (prekey == null)
        {
            logger.LogWarning(
                "Пул one-time prekeys устройства {DeviceId} (user {UserId}) исчерпан — bundle отдан без OneTimePrekey",
                deviceGuid, query.UserId);
        }

        return new FetchPrekeyBundleResponse
        {
            Bundle = bundle.ToGrpc(prekey),
            RemainingOneTimePrekeys = remaining,
        };
    }
}
