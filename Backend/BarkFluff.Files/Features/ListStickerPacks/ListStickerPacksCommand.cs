using BarkFluff.Proto.Files;

using MediatR;

namespace BarkFluff.Files.Features.ListStickerPacks;

public class ListStickerPacksCommand : IRequest<ListStickerPacksResponse>
{
    public int Offset { get; set; }

    public int Limit { get; set; }
}
