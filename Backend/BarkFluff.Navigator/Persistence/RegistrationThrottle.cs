using System.Collections.Concurrent;

namespace BarkFluff.Navigator.Persistence;

// Троттлинг регистраций остаётся in-memory (docs/rearch/phase-1/step-1.5-navigator-persistence.md,
// Изменение 3) — потеря состояния при рестарте безвредна. Синглтон отдельно от ServersStorage,
// т.к. ServersStorage теперь Scoped (использует NavigatorContext).
public class RegistrationThrottle
{
    private readonly ConcurrentDictionary<string, DateTime> _lastRegistrationTimes = new();
    private readonly TimeSpan _throttlePeriod;

    public RegistrationThrottle(IConfiguration configuration)
    {
        _throttlePeriod = TimeSpan.FromMinutes(configuration.GetValue<int>("ServerRegistration:ThrottleMinutes", 2));
    }

    public void CheckAndRecord(string key)
    {
        var now = DateTime.UtcNow;

        if (_lastRegistrationTimes.TryGetValue(key, out var lastTime) && now - lastTime < _throttlePeriod)
        {
            var remainingSeconds = (_throttlePeriod - (now - lastTime)).TotalSeconds;
            throw new InvalidOperationException(
                $"Слишком частая регистрация сервера. Повторная попытка возможна через {remainingSeconds:F0} секунд.");
        }

        _lastRegistrationTimes[key] = now;

        CleanupExpiredEntries(now);
    }

    private void CleanupExpiredEntries(DateTime now)
    {
        foreach (var entry in _lastRegistrationTimes)
        {
            if (now - entry.Value > _throttlePeriod)
                _lastRegistrationTimes.TryRemove(entry.Key, out _);
        }
    }
}
