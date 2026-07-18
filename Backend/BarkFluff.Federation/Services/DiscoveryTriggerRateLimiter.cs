using System.Collections.Concurrent;

namespace BarkFluff.Federation.Services;

// Rate-limit discovery-на-лету по неизвестному key_id (docs/rearch/03-discovery.md, "Политика
// обновления"): без него флуд запросами со случайными key_id заставляет ноду долбить чужой well-known.
public class DiscoveryTriggerRateLimiter
{
    private static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<string, DateTime> _lastTriggered = new();

    public bool TryTrigger(string serverName)
    {
        var now = DateTime.UtcNow;
        var last = _lastTriggered.GetOrAdd(serverName, DateTime.MinValue);

        if (now - last < Cooldown)
            return false;

        _lastTriggered[serverName] = now;
        return true;
    }
}
