using StackExchange.Redis;

namespace BarkFluff.Federation.Services;

/// <summary>
/// Лимит запросов <c>FetchFile</c> per-origin (этап 3.2), по образцу
/// <see cref="ChatCreatedQuotaLimiter"/>: счётчик Redis с минутным окном.
/// </summary>
/// <remarks>
/// Отдельный и заметно более строгий бакет, чем у прочих S2S-вызовов: каждый запрос — это
/// чтение из S3 и исходящий трафик, поэтому дешёвый флуд здесь стоит нам дороже всего
/// (риски №20/21).
/// </remarks>
public class FetchFileRateLimiter : IFetchFileRateLimiter
{
    private const int DefaultLimit = 30;

    private readonly IConnectionMultiplexer _redis;
    private readonly IConfiguration _configuration;

    public FetchFileRateLimiter(IConnectionMultiplexer redis, IConfiguration configuration)
    {
        _redis = redis;
        _configuration = configuration;
    }

    public async Task<bool> TryConsumeAsync(string origin)
    {
        var db = _redis.GetDatabase();
        var key = $"fed:fetchfile:{origin}:{DateTime.UtcNow:yyyyMMddHHmm}";

        var count = await db.StringIncrementAsync(key);
        if (count == 1)
        {
            await db.KeyExpireAsync(key, TimeSpan.FromMinutes(2));
        }

        return count <= GetLimit();
    }

    private int GetLimit()
    {
        var raw = _configuration["Federation:FetchFileRateLimitPerOrigin"];
        return int.TryParse(raw, out var parsed) && parsed > 0 ? parsed : DefaultLimit;
    }
}
