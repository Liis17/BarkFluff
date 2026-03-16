using BarkFluff.Files.Mapping;
using BarkFluff.Files.Persistence;
using BarkFluff.Proto.Files;

using MediatR;

namespace BarkFluff.Files.Features.UpdateStickerPack;

public class UpdateStickerPackCommandHandler : IRequestHandler<UpdateStickerPackCommand, UpdateStickerPackResponse>
{
    private readonly StickerPacksStorage _stickerPacksStorage;
    private readonly StickersStorage _stickersStorage;
    private readonly ILogger<UpdateStickerPackCommandHandler> _logger;

    public UpdateStickerPackCommandHandler(
        StickerPacksStorage stickerPacksStorage,
        StickersStorage stickersStorage,
        ILogger<UpdateStickerPackCommandHandler> logger)
    {
        _stickerPacksStorage = stickerPacksStorage;
        _stickersStorage = stickersStorage;
        _logger = logger;
    }

    public async Task<UpdateStickerPackResponse> Handle(UpdateStickerPackCommand request, CancellationToken cancellationToken)
    {
        var pack = await _stickerPacksStorage.GetByIdAsync(request.PackId)
            ?? throw new Exception("Стикерпак не найден");

        pack.Name = request.Name;
        pack.Description = request.Description;
        pack.CoverStickerId = request.CoverStickerId;

        await _stickerPacksStorage.UpdateAsync(pack);

        _logger.LogInformation("Стикерпак {PackId} обновлён", pack.Id);

        return new UpdateStickerPackResponse { Pack = pack.ToGrpc(pack.Stickers.Count) };
    }
}
