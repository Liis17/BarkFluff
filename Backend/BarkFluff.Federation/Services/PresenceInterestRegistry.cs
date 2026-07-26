using System.Collections.Concurrent;

namespace BarkFluff.Federation.Services;

/// <summary>
/// За какими remote-uuid следят подписчики этой ноды (этап 4.3). Наполняется
/// <c>SetPresenceInterest</c> от инстансов Onliner, читается <see cref="BackgroundServices.PresenceStreamManager"/>.
/// </summary>
/// <remarks>
/// Каждый инстанс Onliner присылает ПОЛНЫЙ свой набор (дельты несводимы между инстансами),
/// реестр объединяет наборы живых инстансов. Записи протухают по TTL и выбрасываются лениво при
/// чтении — рестарт инстанса Onliner самолечится, отдельная чистка не нужна.
/// In-memory: состояние восстанавливается следующим heartbeat'ом за считанные секунды.
/// </remarks>
public class PresenceInterestRegistry
{
    private sealed record Entry(IReadOnlyCollection<Guid> Uuids, DateTime ExpiresAt);

    private readonly ConcurrentDictionary<string, Entry> _entries = new();
    private readonly PresenceOptions _options;
    private readonly TimeProvider _time;

    public PresenceInterestRegistry(PresenceOptions options, TimeProvider? timeProvider = null)
    {
        _options = options;
        _time = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// Заменить набор инстанса целиком. Пустой набор — валидное состояние
    /// («инстанс жив, подписчиков нет»), а не сигнал удалить запись.
    /// </summary>
    public void Set(string instanceId, IReadOnlyCollection<Guid> uuids)
    {
        _entries[instanceId] = new Entry(uuids, _time.GetUtcNow().UtcDateTime + _options.InterestTtl);
    }

    /// <summary>Объединение наборов живых инстансов; протухшие выбрасываются здесь же.</summary>
    public HashSet<Guid> GetUnion()
    {
        var now = _time.GetUtcNow().UtcDateTime;
        var union = new HashSet<Guid>();

        foreach (var (instanceId, entry) in _entries)
        {
            if (entry.ExpiresAt <= now)
            {
                _entries.TryRemove(instanceId, out _);
                continue;
            }

            union.UnionWith(entry.Uuids);
        }

        return union;
    }

    /// <summary>Число живых инстансов, отчитавшихся об интересе — для диагностики.</summary>
    public int LiveInstanceCount
    {
        get
        {
            var now = _time.GetUtcNow().UtcDateTime;
            return _entries.Count(e => e.Value.ExpiresAt > now);
        }
    }
}
