using BarkFluff.Files.Features.GetStickerPack;
using BarkFluff.Files.Helpers;
using BarkFluff.Files.Persistence;
using BarkFluff.Proto.Files;
using BarkFluff.Shared.Exceptions.Files;

using MediatR;

namespace BarkFluff.Files.Features.GetStickerPackByName;

/// <summary>Открывает набор по ссылке /addstickers/&lt;short_name&gt;; содержимое отдаёт GetStickerPack.</summary>
public class GetStickerPackByNameCommandHandler : IRequestHandler<GetStickerPackByNameCommand, GetStickerPackResponse>
{
    private readonly StickerPacksStorage _stickerPacksStorage;
    private readonly IMediator _mediator;

    public GetStickerPackByNameCommandHandler(StickerPacksStorage stickerPacksStorage, IMediator mediator)
    {
        _stickerPacksStorage = stickerPacksStorage;
        _mediator = mediator;
    }

    public async Task<GetStickerPackResponse> Handle(GetStickerPackByNameCommand request, CancellationToken cancellationToken)
    {
        var shortName = StickerPackShortName.Normalize(request.ShortName);
        if (!StickerPackShortName.IsValid(shortName))
            throw new StickerPackNotFoundException();

        var pack = await _stickerPacksStorage.GetByShortNameWithoutStickersAsync(shortName, cancellationToken)
            ?? throw new StickerPackNotFoundException();

        return await _mediator.Send(new GetStickerPackCommand { PackId = pack.Id, UserId = request.UserId }, cancellationToken);
    }
}
