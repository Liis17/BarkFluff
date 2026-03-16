using BarkFluff.Proto.Files;

using MediatR;

namespace BarkFluff.Files.Features.DeleteStickerPack;

public class DeleteStickerPackCommand : IRequest<DeleteStickerPackResponse>
{
    public Guid PackId { get; set; }
}
