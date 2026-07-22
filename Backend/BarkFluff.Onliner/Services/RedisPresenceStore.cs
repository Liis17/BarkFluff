using BarkFluff.Onliner.Domain.Entities;
using BarkFluff.Onliner.Domain.Enums;

using StackExchange.Redis;

namespace BarkFluff.Onliner.Services;

/// <summary>
/// Presence в Redis. Sorted set <c>onliner:presence</c>: member = userId, score = last-seen (unix ms).
/// Online = член множества. <c>ZADD</c> возвращает added=true при первом появлении → переход в online;
/// <c>ZREM</c> атомарен → offline-переход дедуплицируется между инстансами (ровно один получит true).
/// Так presence консистентен для всех инстансов (см. docs/scaling/onliner.md).
/// </summary>
public class RedisPresenceStore(IConnectionMultiplexer redis) : IPresenceStore
{
    private const string Key = "onliner:presence";

    private IDatabase Db => redis.GetDatabase();

    public async Task<bool> MarkOnlineAsync(long userId, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        // true => член добавлен впервые (был offline/absent) => переход в online; false => просто heartbeat.
        return await Db.SortedSetAddAsync(Key, userId, now);
    }

    public async Task<bool> SetOfflineAsync(long userId, CancellationToken ct = default)
        => await Db.SortedSetRemoveAsync(Key, userId);

    public async Task<UserOnlineStatus?> GetOnlineAsync(long userId, CancellationToken ct = default)
    {
        var score = await Db.SortedSetScoreAsync(Key, userId);
        if (score is null)
        {
            return null;
        }

        return ToStatus(userId, score.Value);
    }

    public async Task<IReadOnlyList<UserOnlineStatus>> GetStaleUsersAsync(TimeSpan threshold, CancellationToken ct = default)
    {
        var cutoff = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - (long)threshold.TotalMilliseconds;
        var entries = await Db.SortedSetRangeByScoreWithScoresAsync(Key, double.NegativeInfinity, cutoff);
        return entries.Select(e => ToStatus((long)e.Element, e.Score)).ToList();
    }

    public async Task<IReadOnlyCollection<UserOnlineStatus>> GetOnlineSnapshotAsync(CancellationToken ct = default)
    {
        var entries = await Db.SortedSetRangeByScoreWithScoresAsync(Key);
        return entries.Select(e => ToStatus((long)e.Element, e.Score)).ToList();
    }

    public async Task<long> GetOnlineCountAsync(CancellationToken ct = default)
        => await Db.SortedSetLengthAsync(Key);

    private static UserOnlineStatus ToStatus(long userId, double scoreUnixMs) => new()
    {
        UserId = userId,
        Status = StatusTypeId.Online,
        LastSeen = DateTimeOffset.FromUnixTimeMilliseconds((long)scoreUnixMs).UtcDateTime,
    };
}
