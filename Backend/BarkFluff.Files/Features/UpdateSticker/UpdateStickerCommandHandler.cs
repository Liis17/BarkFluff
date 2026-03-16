using BarkFluff.Files.Helpers;
using BarkFluff.Files.Mapping;
using BarkFluff.Files.Persistence;
using BarkFluff.GrpcServer.Settings;
using BarkFluff.Proto.Files;

using MediatR;

namespace BarkFluff.Files.Features.UpdateSticker;

public class UpdateStickerCommandHandler : IRequestHandler<UpdateStickerCommand, UpdateStickerResponse>
{
    private readonly StickersStorage _stickersStorage;
    private readonly IConfiguration _configuration;
    private readonly RunSettings _runSettings;
    private readonly ILogger<UpdateStickerCommandHandler> _logger;

    public UpdateStickerCommandHandler(
        StickersStorage stickersStorage,
        IConfiguration configuration,
        RunSettings runSettings,
        ILogger<UpdateStickerCommandHandler> logger)
    {
        _stickersStorage = stickersStorage;
        _configuration = configuration;
        _runSettings = runSettings;
        _logger = logger;
    }

    public async Task<UpdateStickerResponse> Handle(UpdateStickerCommand request, CancellationToken cancellationToken)
    {
        var sticker = await _stickersStorage.GetByIdAsync(request.StickerId)
            ?? throw new Exception("Стикер не найден");

        sticker.Emoji = request.Emoji;
        await _stickersStorage.UpdateAsync(sticker);

        _logger.LogInformation("Стикер {StickerId} обновлён", sticker.Id);

        var baseUrl = FileUrlHelper.GetPublicBaseUrl(_configuration, _runSettings);
        return new UpdateStickerResponse { Sticker = sticker.ToGrpc(baseUrl) };
    }
}
