using BarkFluff.Files.Persistence;
using BarkFluff.Proto.Files;

using MediatR;

namespace BarkFluff.Files.Features.RemoveMyStickerPack;

public class RemoveMyStickerPackCommandHandler : IRequestHandler<RemoveMyStickerPackCommand, RemoveMyStickerPackResponse>
{
    private readonly UserStickerPacksStorage _userStickerPacksStorage;

    public RemoveMyStickerPackCommandHandler(UserStickerPacksStorage userStickerPacksStorage)
    {
        _userStickerPacksStorage = userStickerPacksStorage;
    }

    public async Task<RemoveMyStickerPackResponse> Handle(RemoveMyStickerPackCommand request, CancellationToken cancellationToken)
    {
        await _userStickerPacksStorage.RemoveAsync(request.UserId, request.PackId, cancellationToken);
        return new RemoveMyStickerPackResponse();
    }
}
