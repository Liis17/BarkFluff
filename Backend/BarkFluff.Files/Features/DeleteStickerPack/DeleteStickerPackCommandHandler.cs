using BarkFluff.Files.Persistence;
using BarkFluff.Proto.Files;

using MediatR;

namespace BarkFluff.Files.Features.DeleteStickerPack;

public class DeleteStickerPackCommandHandler : IRequestHandler<DeleteStickerPackCommand, DeleteStickerPackResponse>
{
    private readonly StickerPacksStorage _stickerPacksStorage;
    private readonly ILogger<DeleteStickerPackCommandHandler> _logger;

    public DeleteStickerPackCommandHandler(StickerPacksStorage stickerPacksStorage, ILogger<DeleteStickerPackCommandHandler> logger)
    {
        _stickerPacksStorage = stickerPacksStorage;
        _logger = logger;
    }

    public async Task<DeleteStickerPackResponse> Handle(DeleteStickerPackCommand request, CancellationToken cancellationToken)
    {
        await _stickerPacksStorage.DeleteAsync(request.PackId);

        _logger.LogInformation("Стикерпак {PackId} удалён (каскадно удалены стикеры)", request.PackId);

        return new DeleteStickerPackResponse();
    }
}
