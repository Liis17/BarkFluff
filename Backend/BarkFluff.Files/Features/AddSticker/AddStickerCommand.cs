using BarkFluff.Proto.Files;

using MediatR;

namespace BarkFluff.Files.Features.AddSticker;

public class AddStickerCommand : IRequest<AddStickerResponse>
{
    public Guid PackId { get; set; }

    public Guid FileId { get; set; }

    public string Emoji { get; set; } = string.Empty;
}
