using StackExchange.Redis;

namespace BarkFluff.Federation.Services;

// Redis-реализация: cooldown — TTL ключа fed:discovery:{server} (аналог SET NX EX 300 из плана
// масштабирования). Ключ общий на все инстансы: при N экземплярах discovery одного сервера
// запустится один раз в пределах окна.
public class RedisDiscoveryTriggerRateLimiter(IConnectionMultiplexer redis) : IDiscoveryTriggerRateLimiter
{
    private static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(5);

    public Task<bool> TryTriggerAsync(string serverName)
        => redis.GetDatabase().StringSetAsync(
            $"fed:discovery:{serverName}",
            "1",
            Cooldown,
            When.NotExists);
}
