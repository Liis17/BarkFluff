using System.Collections.Concurrent;

namespace BarkFluff.ClientStorage.Infrastructure;

/// <summary>
/// Потокобезопасный сборщик метрик. Хранит счётчики (counters) и показатели (gauges).
/// Снимок периодически сбрасывается в Serilog в формате ServiceMetrics — этот формат
/// собирает MetricsCollectorService в админ-панели (Seq SQL по @Message like 'ServiceMetrics%').
/// </summary>
public class MetricsCollector
{
    private readonly ConcurrentDictionary<string, long> _counters = new();
    private readonly ConcurrentDictionary<string, long> _gauges = new();

    public void Increment(string metricName)
        => _counters.AddOrUpdate(metricName, 1, static (_, oldValue) => oldValue + 1);

    public void Add(string metricName, long value)
        => _counters.AddOrUpdate(metricName, value, (_, oldValue) => oldValue + value);

    /// <summary>Устанавливает значение показателя (gauge). Не сбрасывается при Snapshot.</summary>
    public void Set(string metricName, long value)
        => _gauges[metricName] = value;

    /// <summary>Возвращает снимок и сбрасывает счётчики (gauges сохраняются).</summary>
    public Dictionary<string, long> SnapshotAndReset()
    {
        var snapshot = new Dictionary<string, long>();

        foreach (var key in _counters.Keys)
        {
            var value = _counters.TryRemove(key, out var v) ? v : 0;
            if (value != 0)
                snapshot[key] = value;
        }

        foreach (var kvp in _gauges)
            snapshot[kvp.Key] = kvp.Value;

        return snapshot;
    }
}
