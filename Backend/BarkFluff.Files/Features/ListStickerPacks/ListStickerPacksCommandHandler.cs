using BarkFluff.Files.Mapping;
using BarkFluff.Files.Persistence;
using BarkFluff.Proto.Files;

using MediatR;

namespace BarkFluff.Files.Features.ListStickerPacks;

public class ListStickerPacksCommandHandler : IRequestHandler<ListStickerPacksCommand, ListStickerPacksResponse>
{
    private readonly StickerPacksStorage _stickerPacksStorage;

    public ListStickerPacksCommandHandler(StickerPacksStorage stickerPacksStorage)
    {
        _stickerPacksStorage = stickerPacksStorage;
    }

    public async Task<ListStickerPacksResponse> Handle(ListStickerPacksCommand request, CancellationToken cancellationToken)
    {
        var packs = await _stickerPacksStorage.ListAsync(request.Offset, request.Limit);
        var totalCount = await _stickerPacksStorage.GetTotalCountAsync();

        var packIds = packs.Select(p => p.Id).ToList();
        var stickerCounts = await _stickerPacksStorage.GetStickerCountsAsync(packIds);

        var response = new ListStickerPacksResponse { TotalCount = totalCount };
        response.Packs.AddRange(packs.Select(p =>
            p.ToGrpc(stickerCounts.GetValueOrDefault(p.Id, 0))));

        return response;
    }
}
