using BarkFluff.GrpcServer.XAuth;

using StackExchange.Redis;

namespace BarkFluff.Federation.Services;

/// <summary>
/// Redis-реализация single-runner (по образцу Onliner): лидерство — ключ со значением InstanceId
/// и TTL; лидер продлевает TTL на каждом тике, при падении лидера лок истекает и задачу подхватывает
/// другой инстанс.
/// </summary>
public class RedisSingleRunner(IConnectionMultiplexer redis) : ISingleRunner
{
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
