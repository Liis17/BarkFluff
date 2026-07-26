using System.Collections.Concurrent;

namespace BarkFluff.GrpcServer.Metrics;

/// <summary>
/// Потокобезопасный сборщик метрик для сервиса.
/// Хранит счётчики (counters) и показатели (gauges).
/// </summary>
public class MetricsCollector
{
    private readonly ConcurrentDictionary<string, long> _counters = new();
    private readonly ConcurrentDictionary<string, long> _gauges = new();

    /// <summary>
    /// Увеличивает счётчик на 1.
    /// </summary>
    public void Increment(string metricName)
    {
        _counters.AddOrUpdate(metricName, 1, static (_, oldValue) => oldValue + 1);
    }

    /// <summary>
    /// Добавляет значение к счётчику.
    /// </summary>
    public void Add(string metricName, long value)
    {
        _counters.AddOrUpdate(metricName, value, (_, oldValue) => oldValue + value);
    }

    /// <summary>
    /// Устанавливает значение показателя (gauge). Не сбрасывается при SnapshotAndReset.
    /// </summary>
    public void Set(string metricName, long value)
    {
        _gauges[metricName] = value;
    }

    /// <summary>
    /// Возвращает снимок всех метрик и сбрасывает счётчики (gauges сохраняются).
    /// </summary>
    public Dictionary<string, long> SnapshotAndReset() => SnapshotAndReset(out _);

    /// <summary>
    /// Возвращает снимок всех метрик и сбрасывает счётчики (gauges сохраняются).
    /// <paramref name="hadCounterActivity"/> = true, если в снимке был хотя бы один ненулевой counter
    /// (т.е. с прошлого снимка была реальная активность, а не только статичные gauges).
    /// </summary>
    public Dictionary<string, long> SnapshotAndReset(out bool hadCounterActivity)
    {
        var detailed = SnapshotAndResetDetailed(out hadCounterActivity);
        return detailed.Counters
            .Concat(detailed.Gauges)
            .ToDictionary(x => x.Key, x => x.Value);
    }

    /// <summary>
    /// Возвращает снимок с явной семантикой значений.
    /// Counters — дельты с прошлого снимка, gauges — последнее известное состояние.
    /// Этот формат предназначен для внешнего экспорта метрик.
    /// </summary>
    public MetricsSnapshot SnapshotAndResetDetailed(out bool hadCounterActivity)
    {
        var counters = new Dictionary<string, long>();
        var gauges = new Dictionary<string, long>(_gauges);
        hadCounterActivity = false;

        foreach (var key in _counters.Keys)
        {
            var value = _counters.TryRemove(key, out var v) ? v : 0;
            if (value == 0) continue;

            counters[key] = value;
            hadCounterActivity = true;
        }

        return new MetricsSnapshot(counters, gauges);
    }
}

public sealed record MetricsSnapshot(
    IReadOnlyDictionary<string, long> Counters,
    IReadOnlyDictionary<string, long> Gauges);
