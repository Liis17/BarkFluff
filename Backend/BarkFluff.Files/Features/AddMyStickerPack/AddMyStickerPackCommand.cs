using BarkFluff.Proto.Files;

using MediatR;

namespace BarkFluff.Files.Features.AddMyStickerPack;

public class AddMyStickerPackCommand : IRequest<AddMyStickerPackResponse>
{
    public long UserId { get; set; }

    public Guid PackId { get; set; }
}
