using BarkFluff.Proto.Files;

using MediatR;

namespace BarkFluff.Files.Features.GetStickerPackByFile;

public class GetStickerPackByFileCommand : IRequest<GetStickerPackResponse>
{
    public Guid FileId { get; set; }
}
