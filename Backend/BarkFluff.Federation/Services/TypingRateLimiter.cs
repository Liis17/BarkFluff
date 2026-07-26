using StackExchange.Redis;

namespace BarkFluff.Federation.Services;

/// <summary>
/// Лимит входящих typing per-origin (этап 4.4), по образцу <see cref="ChatCreatedQuotaLimiter"/>:
/// счётчик Redis с окном в минуту, ключ на (origin, минута).
/// </summary>
/// <remarks>
/// Typing дешёвый, но именно поэтому и удобен для спама: без лимита чужая нода бесплатно
/// нагружает наши Users/Messages проверками авторства и членства. Всплеск лимита — не инцидент
/// и алертов не требует.
/// </remarks>
public class TypingRateLimiter : ITypingRateLimiter
{
    private readonly IConnectionMultiplexer _redis;
    private readonly PresenceOptions _options;

    public TypingRateLimiter(IConnectionMultiplexer redis, PresenceOptions options)
    {
        _redis = redis;
        _options = options;
    }

    public async Task<bool> TryConsumeAsync(string origin)
    {
        var db = _redis.GetDatabase();
        var key = $"fed:typing:{origin}:{DateTime.UtcNow:yyyyMMddHHmm}";

        var count = await db.StringIncrementAsync(key);
        if (count == 1)
        {
            await db.KeyExpireAsync(key, TimeSpan.FromMinutes(2));
        }

        return count <= _options.TypingRateLimitPerOriginPerMinute;
    }
}
