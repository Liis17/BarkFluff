using BarkFluff.Files.Features.GetStickerPack;
using BarkFluff.Files.Persistence;
using BarkFluff.Proto.Files;

using MediatR;

namespace BarkFluff.Files.Features.GetStickerPackByFile;

/// <summary>
/// Резолвит стикерпак по file_id стикера — так клиент по клику на стикер в чате
/// открывает модалку пака, не зная его id. Делегирует загрузку паку GetStickerPack,
/// чтобы источник содержимого остался один.
/// </summary>
public class GetStickerPackByFileCommandHandler : IRequestHandler<GetStickerPackByFileCommand, GetStickerPackResponse>
{
    private readonly StickersStorage _stickersStorage;
    private readonly IMediator _mediator;

    public GetStickerPackByFileCommandHandler(StickersStorage stickersStorage, IMediator mediator)
    {
        _stickersStorage = stickersStorage;
        _mediator = mediator;
    }

    public async Task<GetStickerPackResponse> Handle(GetStickerPackByFileCommand request, CancellationToken cancellationToken)
    {
        var sticker = await _stickersStorage.GetByFileIdAsync(request.FileId)
            ?? throw new Exception("Стикер не найден");

        return await _mediator.Send(new GetStickerPackCommand { PackId = sticker.StickerPackId }, cancellationToken);
    }
}
