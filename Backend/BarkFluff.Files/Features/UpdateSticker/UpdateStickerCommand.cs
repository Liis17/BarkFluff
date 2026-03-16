using BarkFluff.Proto.Files;

using MediatR;

namespace BarkFluff.Files.Features.UpdateSticker;

public class UpdateStickerCommand : IRequest<UpdateStickerResponse>
{
    public Guid StickerId { get; set; }

    public string Emoji { get; set; } = string.Empty;
}
