using BarkFluff.Bots.Infrastructure;
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
    private readonly BotTokenIssuer _tokenIssuer;
    private readonly ILogger<RegenerateTokenCommandHandler> _logger;

    public RegenerateTokenCommandHandler(
        BotsStorage botsStorage,
        BotRegistryCache registryCache,
        BotTokenIssuer tokenIssuer,
        ILogger<RegenerateTokenCommandHandler> logger)
    {
        _botsStorage = botsStorage;
        _registryCache = registryCache;
        _tokenIssuer = tokenIssuer;
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

        var (token, tokenId) = await _tokenIssuer.IssueAsync(bot.Id, cancellationToken);
        bot.TokenId = tokenId;

        await _botsStorage.Update(bot);
        _registryCache.Set(bot);

        _logger.LogInformation("Токен бота {Username} (id {BotId}) перегенерирован", bot.Username, bot.Id);

        return new RegenerateTokenResponse { Token = token };
    }
}
