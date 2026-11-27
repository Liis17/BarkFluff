using BarkFluff.Proto.Files;

using MediatR;

namespace BarkFluff.Files.Features.GetStickerPack;

public class GetStickerPackCommand : IRequest<GetStickerPackResponse>
{
    public Guid PackId { get; set; }

    /// <summary>Пользователь, для которого заполняется IsAdded; null — служебный запрос.</summary>
    public long? UserId { get; set; }
}
