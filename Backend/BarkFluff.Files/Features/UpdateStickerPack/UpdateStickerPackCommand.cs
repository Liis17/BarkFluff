using BarkFluff.Proto.Files;

using MediatR;

namespace BarkFluff.Files.Features.UpdateStickerPack;

public class UpdateStickerPackCommand : IRequest<UpdateStickerPackResponse>
{
    public Guid PackId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public Guid? CoverStickerId { get; set; }
}
