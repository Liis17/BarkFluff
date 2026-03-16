using BarkFluff.Files.Helpers;
using BarkFluff.Files.Mapping;
using BarkFluff.Files.Persistence;
using BarkFluff.GrpcServer.Settings;
using BarkFluff.Proto.Files;

using MediatR;

namespace BarkFluff.Files.Features.GetStickers;

public class GetStickersCommandHandler : IRequestHandler<GetStickersCommand, GetStickersResponse>
{
    private readonly StickersStorage _stickersStorage;
    private readonly IConfiguration _configuration;
    private readonly RunSettings _runSettings;

    public GetStickersCommandHandler(
        StickersStorage stickersStorage,
        IConfiguration configuration,
        RunSettings runSettings)
    {
        _stickersStorage = stickersStorage;
        _configuration = configuration;
        _runSettings = runSettings;
    }

    public async Task<GetStickersResponse> Handle(GetStickersCommand request, CancellationToken cancellationToken)
    {
        var stickers = await _stickersStorage.GetByIdsAsync(request.StickerIds);

        var baseUrl = FileUrlHelper.GetPublicBaseUrl(_configuration, _runSettings);

        var response = new GetStickersResponse();
        response.Stickers.AddRange(stickers.Select(s => s.ToGrpc(baseUrl)));

        return response;
    }
}
