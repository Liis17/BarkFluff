using BarkFluff.Files.Persistence;
using BarkFluff.Proto.Files;
using BarkFluff.Shared.Exceptions.Files;

using MediatR;

namespace BarkFluff.Files.Features.AddMyStickerPack;

public class AddMyStickerPackCommandHandler : IRequestHandler<AddMyStickerPackCommand, AddMyStickerPackResponse>
{
    /// <summary>ListMyStickerPacks отдаёт наборы вместе со стикерами одним ответом — число наборов ограничено.</summary>
    public const int MaxPacksPerUser = 200;

    private readonly StickerPacksStorage _stickerPacksStorage;
    private readonly UserStickerPacksStorage _userStickerPacksStorage;

    public AddMyStickerPackCommandHandler(StickerPacksStorage stickerPacksStorage, UserStickerPacksStorage userStickerPacksStorage)
    {
        _stickerPacksStorage = stickerPacksStorage;
        _userStickerPacksStorage = userStickerPacksStorage;
    }

    public async Task<AddMyStickerPackResponse> Handle(AddMyStickerPackCommand request, CancellationToken cancellationToken)
    {
        _ = await _stickerPacksStorage.GetByIdWithoutStickersAsync(request.PackId)
            ?? throw new StickerPackNotFoundException();

        if (await _userStickerPacksStorage.ExistsAsync(request.UserId, request.PackId, cancellationToken))
            return new AddMyStickerPackResponse();

        if (await _userStickerPacksStorage.CountAsync(request.UserId, cancellationToken) >= MaxPacksPerUser)
            throw new StickerPackLimitExceededException();

        await _userStickerPacksStorage.TryAddAsync(request.UserId, request.PackId, cancellationToken);
        return new AddMyStickerPackResponse();
    }
}
