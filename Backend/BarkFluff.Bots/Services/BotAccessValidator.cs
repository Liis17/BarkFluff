using System.Security.Claims;

using BarkFluff.Bots.Domain;
using BarkFluff.Shared.Identity;

namespace BarkFluff.Bots.Services;

/// <summary>
/// Проверка доступа бота после штатной JWT-валидации XAuth (общая для gRPC-интерцептора
/// и HTTP endpoint-фильтра): тип токена Bot, бот существует и не системный,
/// claim x-bot-token-id совпадает с актуальным TokenId (мгновенный отзыв) + rate-limit.
/// </summary>
public class BotAccessValidator
{
    private readonly BotRegistryCache _registryCache;
    private readonly BotRateLimiter _rateLimiter;

    public BotAccessValidator(BotRegistryCache registryCache, BotRateLimiter rateLimiter)
    {
        _registryCache = registryCache;
        _rateLimiter = rateLimiter;
    }

    public BotAccessStatus Validate(ClaimsPrincipal principal)
    {
        if (principal.FindFirst(IdentityClaims.TokenType)?.Value != TokenType.Bot.ToString())
            return BotAccessStatus.Unauthenticated;

        if (!long.TryParse(principal.FindFirst(IdentityClaims.UserId)?.Value, out var botId))
            return BotAccessStatus.Unauthenticated;

        var bot = _registryCache.Get(botId);

        if (bot is null || bot.SystemRole != SystemBotRole.None)
            return BotAccessStatus.Unauthenticated;

        var tokenId = principal.FindFirst(IdentityClaims.BotTokenId)?.Value;

        if (string.IsNullOrEmpty(tokenId) || tokenId != bot.TokenId)
            return BotAccessStatus.Unauthenticated;

        return _rateLimiter.TryAcquire(botId)
            ? BotAccessStatus.Allowed
            : BotAccessStatus.RateLimited;
    }
}

public enum BotAccessStatus
{
    Allowed,
    Unauthenticated,
    RateLimited,
}
