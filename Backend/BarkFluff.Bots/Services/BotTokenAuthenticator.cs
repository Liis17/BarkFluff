using BarkFluff.Bots.Domain;

namespace BarkFluff.Bots.Services;

/// <summary>
/// Проверка токена бота (общая для gRPC-интерцептора и HTTP endpoint-фильтра).
/// Системные боты работают in-process и внешним токеном не аутентифицируются.
/// </summary>
public class BotTokenAuthenticator
{
    private readonly BotTokenService _tokenService;
    private readonly BotRegistryCache _registryCache;

    public BotTokenAuthenticator(BotTokenService tokenService, BotRegistryCache registryCache)
    {
        _tokenService = tokenService;
        _registryCache = registryCache;
    }

    public Bot? Authenticate(string? token)
    {
        if (!_tokenService.TryParseToken(token, out var botId, out var secret))
            return null;

        var bot = _registryCache.Get(botId);

        if (bot is null || bot.SystemRole != SystemBotRole.None)
            return null;

        return _tokenService.VerifySecret(secret, bot.TokenHash) ? bot : null;
    }
}
