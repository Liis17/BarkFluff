using BarkFluff.Files.Helpers;
using BarkFluff.Files.Mapping;
using BarkFluff.Files.Persistence;
using BarkFluff.GrpcServer.Settings;
using BarkFluff.Proto.Files;
using BarkFluff.Shared.Exceptions.Files;

using MediatR;

namespace BarkFluff.Files.Features.GetStickerPack;

public class GetStickerPackCommandHandler : IRequestHandler<GetStickerPackCommand, GetStickerPackResponse>
{
    private readonly StickerPacksStorage _stickerPacksStorage;
    private readonly UserStickerPacksStorage _userStickerPacksStorage;
    private readonly IConfiguration _configuration;
    private readonly RunSettings _runSettings;

    public GetStickerPackCommandHandler(
        StickerPacksStorage stickerPacksStorage,
        UserStickerPacksStorage userStickerPacksStorage,
        IConfiguration configuration,
        RunSettings runSettings)
    {
        _stickerPacksStorage = stickerPacksStorage;
        _userStickerPacksStorage = userStickerPacksStorage;
        _configuration = configuration;
        _runSettings = runSettings;
    }

    public async Task<GetStickerPackResponse> Handle(GetStickerPackCommand request, CancellationToken cancellationToken)
    {
        var pack = await _stickerPacksStorage.GetByIdAsync(request.PackId)
            ?? throw new StickerPackNotFoundException();

        var baseUrl = FileUrlHelper.GetPublicBaseUrl(_configuration, _runSettings);

        var response = new GetStickerPackResponse
        {
            Pack = pack.ToGrpc(pack.Stickers.Count),
            IsAdded = request.UserId is { } userId
                && await _userStickerPacksStorage.ExistsAsync(userId, pack.Id, cancellationToken)
        };

        response.Stickers.AddRange(pack.Stickers.Select(s => s.ToGrpc(baseUrl)));

        return response;
    }
}
