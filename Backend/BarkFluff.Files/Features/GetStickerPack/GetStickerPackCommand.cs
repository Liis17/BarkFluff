using BarkFluff.Proto.Files;

using MediatR;

namespace BarkFluff.Files.Features.GetStickerPack;

public class GetStickerPackCommand : IRequest<GetStickerPackResponse>
{
    public Guid PackId { get; set; }
}
