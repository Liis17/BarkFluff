using BarkFluff.Proto.Files;

using MediatR;

namespace BarkFluff.Files.Features.ListMyStickerPacks;

public class ListMyStickerPacksCommand : IRequest<ListMyStickerPacksResponse>
{
    public long UserId { get; set; }
}
