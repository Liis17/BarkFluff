using BarkFluff.GrpcServer.XAuth;

using StackExchange.Redis;

namespace BarkFluff.Onliner.Services;

/// <summary>
/// Лёгкий распределённый single-runner на Redis: одну периодическую фоновую задачу
/// (offline-детекция, персист last-seen) выполняет один инстанс, остальные пропускают тик.
/// Best-effort: при редком кратковременном двойном лидерстве задачи остаются корректными
/// (ZREM атомарен и дедуплицирует offline-переход, upsert last-seen идемпотентен).
/// </summary>
public class RedisSingleRunner(IConnectionMultiplexer redis)
{
    /// <summary>Стать/остаться лидером для ключа на ttl. true — этот инстанс выполняет задачу в этот тик.</summary>
    public async Task<bool> TryAcquireAsync(string lockKey, TimeSpan ttl)
    {
        var db = redis.GetDatabase();

        // Свободен — захватываем лидерство.
        if (await db.StringSetAsync(lockKey, InstanceId.Current, ttl, When.NotExists))
        {
            return true;
        }

        // Держим сами — продлеваем лидерство на следующий тик.
        if (await db.StringGetAsync(lockKey) == InstanceId.Current)
        {
            await db.KeyExpireAsync(lockKey, ttl);
            return true;
        }

        return false;
    }
}
