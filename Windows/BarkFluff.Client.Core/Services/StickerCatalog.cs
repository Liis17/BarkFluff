using BarkFluff.Proto.Files;
using WebApiClient = BarkFluff.WebApi.Core.WebApi;

namespace BarkFluff.Client.Core.Services;

public interface IStickerCatalog
{
    Task<IReadOnlyList<StickerPackInfo>?> ListAsync(string node, long user, CancellationToken token);
    Task<IReadOnlyList<StickerInfo>?> LoadPackAsync(string node, long user, string packId, CancellationToken token);
}

public sealed class StickerCatalog(WebApiClient webApi, IClientSession session) : IStickerCatalog
{
    public async Task<IReadOnlyList<StickerPackInfo>?> ListAsync(string node, long user, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (session.CurrentConnection is not { } connection ||
            connection.Profile.BeaconAddress != node || connection.ConnectionParameters.UserId != user)
            return null;

        var result = await webApi.ListStickerPacksAsync(connection.ConnectionParameters, offset: 0, size: 50);
        return result.error is { IsSuccess: true } ? result.packs : null;
    }

    public async Task<IReadOnlyList<StickerInfo>?> LoadPackAsync(string node, long user, string packId, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (session.CurrentConnection is not { } connection ||
            connection.Profile.BeaconAddress != node || connection.ConnectionParameters.UserId != user)
            return null;

        var result = await webApi.GetStickerPackAsync(connection.ConnectionParameters, packId);
        return result.error is { IsSuccess: true } ? result.stickers : null;
    }
}
