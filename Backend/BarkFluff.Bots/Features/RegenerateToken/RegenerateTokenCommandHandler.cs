using BarkFluff.Bots.Persistence.Services;
using BarkFluff.Bots.Services;
using BarkFluff.Proto.Bots;
using BarkFluff.Shared.Exceptions.Bots;

using MediatR;

namespace BarkFluff.Bots.Features.RegenerateToken;

public class RegenerateTokenCommandHandler : IRequestHandler<RegenerateTokenCommand, RegenerateTokenResponse>
{
    private readonly BotsStorage _botsStorage;
    private readonly BotRegistryCache _registryCache;
    private readonly BotTokenService _tokenService;
    private readonly ILogger<RegenerateTokenCommandHandler> _logger;

    public RegenerateTokenCommandHandler(
        BotsStorage botsStorage,
        BotRegistryCache registryCache,
        BotTokenService tokenService,
        ILogger<RegenerateTokenCommandHandler> logger)
    {
        _botsStorage = botsStorage;
        _registryCache = registryCache;
        _tokenService = tokenService;
        _logger = logger;
    }

    public async Task<RegenerateTokenResponse> Handle(RegenerateTokenCommand request, CancellationToken cancellationToken)
    {
        var bot = await _botsStorage.GetById(request.BotId);

        if (bot is null)
        {
            _logger.LogWarning("Бот {BotId} не найден", request.BotId);
            throw new BotNotFoundException();
        }

        var (token, tokenHash) = _tokenService.GenerateToken(bot.Id);
        bot.TokenHash = tokenHash;

        await _botsStorage.Update(bot);
        _registryCache.Set(bot);

        _logger.LogInformation("Токен бота {Username} (id {BotId}) перегенерирован", bot.Username, bot.Id);

        return new RegenerateTokenResponse { Token = token };
    }
}
