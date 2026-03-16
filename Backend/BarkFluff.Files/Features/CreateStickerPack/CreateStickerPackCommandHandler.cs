using BarkFluff.Files.Domain;
using BarkFluff.Files.Mapping;
using BarkFluff.Files.Persistence;
using BarkFluff.Proto.Files;

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
        var pack = new StickerPack
        {
            Id = Guid.NewGuid(),
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
