using BarkFluff.GrpcServer.XAuth;

using StackExchange.Redis;

namespace BarkFluff.Bots.Services;

/// <summary>
/// Один активный поток получения update'ов на бота глобально (как в Telegram): concurrent
/// getUpdates/SubscribeUpdates запрещён. Снимает и гонку по LastConfirmedUpdateId.
/// </summary>
public interface IBotPollingGuard
{
    /// <summary>true = слот занят этим инстансом; false = уже есть активный поток (на любом инстансе).</summary>
    Task<bool> TryEnterAsync(long botId);

    /// <summary>Продлить владение слотом (для долгоживущих стримов — иначе TTL истечёт).</summary>
    Task RenewAsync(long botId);

    Task ExitAsync(long botId);
}

/// <summary>
/// Redis-реализация распределённого лока: <c>SET pollguard:{botId} {instanceId} NX EX</c>.
/// TTL переживает падение инстанса (слот освобождается), долгие стримы продлевают его через Renew.
/// </summary>
public class RedisBotPollingGuard(IConnectionMultiplexer redis) : IBotPollingGuard
{
    // Больше максимального long-poll (50с); долгие стримы продлевают TTL в цикле.
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(90);

    public Task<bool> TryEnterAsync(long botId)
        => redis.GetDatabase().StringSetAsync(Key(botId), InstanceId.Current, Ttl, When.NotExists);

    public async Task RenewAsync(long botId)
    {
        var db = redis.GetDatabase();
        // Продлеваем только свой слот.
        if (await db.StringGetAsync(Key(botId)) == InstanceId.Current)
        {
            await db.KeyExpireAsync(Key(botId), Ttl);
        }
    }

    public async Task ExitAsync(long botId)
    {
        var db = redis.GetDatabase();
        // Удаляем только свой слот (не затираем чужой, если TTL истёк и слот перезахвачен).
        if (await db.StringGetAsync(Key(botId)) == InstanceId.Current)
        {
            await db.KeyDeleteAsync(Key(botId));
        }
    }

    private static string Key(long botId) => $"pollguard:{botId}";
}
