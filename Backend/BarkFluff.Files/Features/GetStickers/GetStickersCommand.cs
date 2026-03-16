using BarkFluff.Proto.Files;

using MediatR;

namespace BarkFluff.Files.Features.GetStickers;

public class GetStickersCommand : IRequest<GetStickersResponse>
{
    public List<Guid> StickerIds { get; set; } = new();
}
