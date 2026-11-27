using BarkFluff.Proto.Files;

using MediatR;

namespace BarkFluff.Files.Features.GetStickerPackByName;

public class GetStickerPackByNameCommand : IRequest<GetStickerPackResponse>
{
    public string ShortName { get; set; } = string.Empty;

    public long UserId { get; set; }
}
