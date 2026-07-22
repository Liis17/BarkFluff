using StackExchange.Redis;

namespace BarkFluff.Bots.Services;

/// <summary>
/// Per-bot rate-limit внешнего API (общий для gRPC-интерцептора и HTTP endpoint-фильтра).
/// Fixed window 1 секунда, 30 запросов на бота (как у Telegram).
/// </summary>
public interface IBotRateLimiter
{
    Task<bool> TryAcquireAsync(long botId);
}

/// <summary>
/// Redis-реализация: общий счётчик на все инстансы (иначе бот получал бы 30×N req/s).
/// Скользящее окно через INCR ключа <c>botrate:{botId}:{unixSecond}</c> + EXPIRE; ключ на секунду
/// самоочищается (см. docs/scaling/bots.md).
/// </summary>
public class RedisBotRateLimiter(IConnectionMultiplexer redis) : IBotRateLimiter
{
    private const int RequestsPerSecond = 30;

    public async Task<bool> TryAcquireAsync(long botId)
    {
        var db = redis.GetDatabase();
        var second = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var key = $"botrate:{botId}:{second}";

        var count = await db.StringIncrementAsync(key);
        if (count == 1)
        {
            // TTL с запасом на границу секунды — ключ живёт недолго и самоочищается.
            await db.KeyExpireAsync(key, TimeSpan.FromSeconds(2));
        }

        return count <= RequestsPerSecond;
    }
}
