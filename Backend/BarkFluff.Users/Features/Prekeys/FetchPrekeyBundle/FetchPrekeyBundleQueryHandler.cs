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
            throw new PrekeyException(Grpc.Core.StatusCode.InvalidArgument, "Некорректный DeviceId");
        }

        var result = await prekeyStorage.FetchBundleAsync(query.UserId, deviceGuid, query.RequirePqxdh, cancellationToken);

        if (result == null)
        {
            throw new PrekeyException(Grpc.Core.StatusCode.NotFound, "Bundle устройства не найден");
        }

        var (bundle, prekey, kyberPrekey, kyberPrekeyIsLastResort, remainingEc, remainingKyber) = result;

        if (prekey == null)
        {
            logger.LogWarning(
                "Пул one-time prekeys устройства {DeviceId} (user {UserId}) исчерпан — bundle отдан без OneTimePrekey",
                deviceGuid, query.UserId);
        }

        if (kyberPrekeyIsLastResort)
        {
            logger.LogWarning(
                "Kyber pool устройства {DeviceId} (user {UserId}) исчерпан — возвращён last-resort prekey",
                deviceGuid, query.UserId);
        }

        return new FetchPrekeyBundleResponse
        {
            Bundle = bundle.ToGrpc(prekey, kyberPrekey, kyberPrekeyIsLastResort),
            RemainingOneTimePrekeys = remainingEc,
            RemainingOneTimeKyberPrekeys = remainingKyber,
        };
    }
}
