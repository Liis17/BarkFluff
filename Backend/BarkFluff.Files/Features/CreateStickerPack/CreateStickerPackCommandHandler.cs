using BarkFluff.Files.Domain;
using BarkFluff.Files.Helpers;
using BarkFluff.Files.Mapping;
using BarkFluff.Files.Persistence;
using BarkFluff.Proto.Files;
using BarkFluff.Shared.Exceptions.Files;

using MediatR;

namespace BarkFluff.Files.Features.CreateStickerPack;

public class CreateStickerPackCommandHandler : IRequestHandler<CreateStickerPackCommand, CreateStickerPackResponse>
{
    private readonly StickerPacksStorage _stickerPacksStorage;
    private readonly ILogger<CreateStickerPackCommandHandler> _logger;

    public CreateStickerPackCommandHandler(StickerPacksStorage stickerPacksStorage, ILogger<CreateStickerPackCommandHandler> logger)
    {
        _stickerPacksStorage = stickerPacksStorage;
        _logger = logger;
    }

    public async Task<CreateStickerPackResponse> Handle(CreateStickerPackCommand request, CancellationToken cancellationToken)
    {
        var packId = Guid.NewGuid();
        var shortName = string.IsNullOrWhiteSpace(request.ShortName)
            ? StickerPackShortName.Generate(packId)
            : StickerPackShortName.Normalize(request.ShortName);

        if (!StickerPackShortName.IsValid(shortName))
            throw new StickerPackShortNameNotValidException();
        if (await _stickerPacksStorage.IsShortNameTakenAsync(shortName, cancellationToken: cancellationToken))
            throw new StickerPackShortNameTakenException();

        var pack = new StickerPack
        {
            Id = packId,
            ShortName = shortName,
            CreatorUserId = request.CreatorUserId,
            Name = request.Name,
            Description = request.Description,
            CreatedAt = DateTime.UtcNow
        };

        await _stickerPacksStorage.AddAsync(pack);

        _logger.LogInformation("Стикерпак {PackId} создан пользователем {UserId}", pack.Id, request.CreatorUserId);

        return new CreateStickerPackResponse { Pack = pack.ToGrpc() };
    }
}
