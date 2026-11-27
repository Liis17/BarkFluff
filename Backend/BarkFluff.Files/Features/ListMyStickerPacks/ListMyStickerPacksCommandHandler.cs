using BarkFluff.Files.Helpers;
using BarkFluff.Files.Mapping;
using BarkFluff.Files.Persistence;
using BarkFluff.GrpcServer.Settings;
using BarkFluff.Proto.Files;

using MediatR;

namespace BarkFluff.Files.Features.ListMyStickerPacks;

public class ListMyStickerPacksCommandHandler : IRequestHandler<ListMyStickerPacksCommand, ListMyStickerPacksResponse>
{
    private readonly UserStickerPacksStorage _userStickerPacksStorage;
    private readonly IConfiguration _configuration;
    private readonly RunSettings _runSettings;

    public ListMyStickerPacksCommandHandler(
        UserStickerPacksStorage userStickerPacksStorage,
        IConfiguration configuration,
        RunSettings runSettings)
    {
        _userStickerPacksStorage = userStickerPacksStorage;
        _configuration = configuration;
        _runSettings = runSettings;
    }

    public async Task<ListMyStickerPacksResponse> Handle(ListMyStickerPacksCommand request, CancellationToken cancellationToken)
    {
        var packs = await _userStickerPacksStorage.ListPacksWithStickersAsync(request.UserId, cancellationToken);
        var baseUrl = FileUrlHelper.GetPublicBaseUrl(_configuration, _runSettings);

        var response = new ListMyStickerPacksResponse();
        foreach (var pack in packs)
        {
            var content = new StickerPackContent { Pack = pack.ToGrpc(pack.Stickers.Count) };
            content.Stickers.AddRange(pack.Stickers.Select(s => s.ToGrpc(baseUrl)));
            response.Packs.Add(content);
        }

        return response;
    }
}
