using BarkFluff.Files.Persistence;
using BarkFluff.Proto.Files;

using MediatR;

namespace BarkFluff.Files.Features.RemoveSticker;

public class RemoveStickerCommandHandler : IRequestHandler<RemoveStickerCommand, RemoveStickerResponse>
{
    private readonly StickersStorage _stickersStorage;
    private readonly StickerPacksStorage _stickerPacksStorage;
    private readonly ILogger<RemoveStickerCommandHandler> _logger;

    public RemoveStickerCommandHandler(
        StickersStorage stickersStorage,
        StickerPacksStorage stickerPacksStorage,
        ILogger<RemoveStickerCommandHandler> logger)
    {
        _stickersStorage = stickersStorage;
        _stickerPacksStorage = stickerPacksStorage;
        _logger = logger;
    }

    public async Task<RemoveStickerResponse> Handle(RemoveStickerCommand request, CancellationToken cancellationToken)
    {
        var sticker = await _stickersStorage.GetByIdAsync(request.StickerId);

        if (sticker is not null)
        {
            // Если удаляемый стикер — обложка пака, сбрасываем обложку
            var pack = await _stickerPacksStorage.GetByIdAsync(sticker.StickerPackId);
            if (pack is not null && pack.CoverStickerId == sticker.Id)
            {
                pack.CoverStickerId = null;
                await _stickerPacksStorage.UpdateAsync(pack);
            }

            await _stickersStorage.DeleteAsync(request.StickerId);

            _logger.LogInformation("Стикер {StickerId} удалён из пака {PackId}", request.StickerId, sticker.StickerPackId);
        }

        return new RemoveStickerResponse();
    }
}
