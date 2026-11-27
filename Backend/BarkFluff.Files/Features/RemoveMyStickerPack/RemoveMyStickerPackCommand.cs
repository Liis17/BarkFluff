using BarkFluff.Proto.Files;

using MediatR;

namespace BarkFluff.Files.Features.RemoveMyStickerPack;

public class RemoveMyStickerPackCommand : IRequest<RemoveMyStickerPackResponse>
{
    public long UserId { get; set; }

    public Guid PackId { get; set; }
}
