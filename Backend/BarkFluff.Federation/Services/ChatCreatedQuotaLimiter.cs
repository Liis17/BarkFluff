using StackExchange.Redis;

namespace BarkFluff.Federation.Services;

// Квота ChatCreated per-origin (этап 2.5, docs/rearch/phase-2/step-2.5-privacy-antispam.md,
// «Изменение 4»): защита от спам-волны создания чатов одной нодой. Часовое скользящее окно —
// счётчик Redis с TTL, ключ на (origin, час).
public class ChatCreatedQuotaLimiter : IChatCreatedQuotaLimiter
{
    private const int DefaultLimit = 100;

    private readonly IConnectionMultiplexer _redis;
    private readonly IConfiguration _configuration;

    public ChatCreatedQuotaLimiter(IConnectionMultiplexer redis, IConfiguration configuration)
    {
        _redis = redis;
        _configuration = configuration;
    }

    /// <summary>Инкрементирует счётчик origin за текущий час; false — квота на этот час исчерпана.</summary>
    public async Task<bool> TryConsumeAsync(string origin)
    {
        var db = _redis.GetDatabase();
        var key = $"fed:chatcreated:{origin}:{DateTime.UtcNow:yyyyMMddHH}";

        var count = await db.StringIncrementAsync(key);
        if (count == 1)
            await db.KeyExpireAsync(key, TimeSpan.FromHours(1));

        return count <= GetLimit();
    }

    private int GetLimit()
    {
        var raw = _configuration["Federation:ChatCreatedHourlyLimit"];
        return int.TryParse(raw, out var parsed) && parsed > 0 ? parsed : DefaultLimit;
    }
}
