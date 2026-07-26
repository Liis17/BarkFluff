using BarkFluff.Onliner.Domain.Enums;

using StackExchange.Redis;

namespace BarkFluff.Onliner.Services;

/// <summary>
/// Remote-статусы в Redis: ключ на пользователя <c>onliner:presence:remote:{uuid}</c>,
/// значение — <c>"{status}:{lastSeenUnixMs}"</c>, TTL продлевается каждым обновлением.
/// </summary>
/// <remarks>
/// Намеренно НЕ в sorted set <c>onliner:presence</c>: тот обслуживает
/// <see cref="BackgroundServices.OfflineDetectionService"/>, гасящий записи через 5 секунд без
/// heartbeat. У remote-пользователей heartbeat'ов нет (истина на чужой ноде) — они уходили бы в
/// offline мгновенно. TTL-ключи заодно дают эвикцию uuid без подписчиков без фонового сервиса.
/// </remarks>
public class RedisRemotePresenceStore : IRemotePresenceStore
{
    private const string KeyPrefix = "onliner:presence:remote:";

    private readonly IConnectionMultiplexer _redis;
    private readonly TimeSpan _ttl;

    public RedisRemotePresenceStore(IConnectionMultiplexer redis, IConfiguration configuration)
    {
        _redis = redis;

        var seconds = configuration.GetValue<int?>("Onliner:RemotePresenceTtlSeconds") ?? 900;
        _ttl = TimeSpan.FromSeconds(seconds > 0 ? seconds : 900);
    }

    private IDatabase Db => _redis.GetDatabase();

    private static RedisKey KeyFor(Guid uuid) => KeyPrefix + uuid.ToString("D");

    public Task UpsertAsync(Guid uuid, StatusTypeId status, DateTime lastSeen, CancellationToken ct = default)
    {
        var value = $"{(int)status}:{new DateTimeOffset(lastSeen.ToUniversalTime()).ToUnixTimeMilliseconds()}";
        return Db.StringSetAsync(KeyFor(uuid), value, _ttl);
    }

    public async Task<RemotePresenceEntry?> GetAsync(Guid uuid, CancellationToken ct = default)
    {
        var raw = await Db.StringGetAsync(KeyFor(uuid));
        return TryParse(raw, out var entry) ? entry : null;
    }

    public async Task<IReadOnlyDictionary<Guid, RemotePresenceEntry>> GetManyAsync(
        IReadOnlyCollection<Guid> uuids,
        CancellationToken ct = default)
    {
        var result = new Dictionary<Guid, RemotePresenceEntry>();

        if (uuids.Count == 0)
        {
            return result;
        }

        var ordered = uuids.Distinct().ToList();
        var values = await Db.StringGetAsync(ordered.Select(KeyFor).ToArray());

        for (var i = 0; i < ordered.Count; i++)
        {
            if (TryParse(values[i], out var entry))
            {
                result[ordered[i]] = entry;
            }
        }

        return result;
    }

    public Task RemoveAsync(Guid uuid, CancellationToken ct = default)
        => Db.KeyDeleteAsync(KeyFor(uuid));

    private static bool TryParse(RedisValue raw, out RemotePresenceEntry entry)
    {
        entry = default;

        if (raw.IsNullOrEmpty)
        {
            return false;
        }

        var text = raw.ToString();
        var separator = text.IndexOf(':');

        if (separator <= 0
            || !int.TryParse(text.AsSpan(0, separator), out var status)
            || !long.TryParse(text.AsSpan(separator + 1), out var lastSeenUnixMs))
        {
            return false;
        }

        entry = new RemotePresenceEntry(
            (StatusTypeId)status,
            DateTimeOffset.FromUnixTimeMilliseconds(lastSeenUnixMs).UtcDateTime);

        return true;
    }
}
