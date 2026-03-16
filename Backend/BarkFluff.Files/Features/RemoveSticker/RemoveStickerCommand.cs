using BarkFluff.Proto.Files;

using MediatR;

namespace BarkFluff.Files.Features.RemoveSticker;

public class RemoveStickerCommand : IRequest<RemoveStickerResponse>
{
    public Guid StickerId { get; set; }
}
