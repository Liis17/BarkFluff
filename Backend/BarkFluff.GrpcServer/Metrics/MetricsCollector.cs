using System.Collections.Concurrent;
using System.Threading.Channels;

namespace BarkFluff.GrpcServer.Metrics;

/// <summary>
/// Потокобезопасный сборщик метрик для сервиса.
/// Хранит счётчики (counters) и показатели (gauges).
/// </summary>
public class MetricsCollector
{
    private readonly ConcurrentDictionary<string, long> _bufferedCounters = new();
    private readonly ConcurrentDictionary<string, long> _gauges = new();
    private readonly ConcurrentDictionary<string, long> _changedGauges = new();
    private readonly object _gaugesLock = new();
    private readonly Channel<MetricsSnapshot> _immediateSnapshots = Channel.CreateUnbounded<MetricsSnapshot>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
    private readonly MetricsExportProfile _profile;

    public MetricsCollector() : this(MetricsExportProfile.BufferAll()) { }

    public MetricsCollector(MetricsExportProfile profile)
    {
        _profile = profile;
    }

    /// <summary>
    /// Увеличивает счётчик на 1.
    /// </summary>
    public void Increment(string metricName)
    {
        Add(metricName, 1);
    }

    /// <summary>
    /// Добавляет значение к счётчику.
    /// </summary>
    public void Add(string metricName, long value)
    {
        if (value == 0) return;

        if (_profile.IsBuffered(metricName))
        {
            _bufferedCounters.AddOrUpdate(metricName, value, (_, oldValue) => oldValue + value);
            return;
        }

        _immediateSnapshots.Writer.TryWrite(new MetricsSnapshot(
            new Dictionary<string, long> { [metricName] = value },
            new Dictionary<string, long>()));
    }

    /// <summary>
    /// Устанавливает значение показателя (gauge). Не сбрасывается при SnapshotAndReset.
    /// </summary>
    public void Set(string metricName, long value)
    {
        SetMany(new KeyValuePair<string, long>(metricName, value));
    }

    /// <summary>
    /// Атомарно устанавливает связанные показатели, чтобы экспорт не смешивал значения
    /// разных операций в одном снимке.
    /// </summary>
    public void SetMany(params KeyValuePair<string, long>[] gauges)
    {
        lock (_gaugesLock)
        {
            foreach (var (metricName, value) in gauges)
            {
                if (!_gauges.TryGetValue(metricName, out var oldValue) || oldValue != value)
                {
                    _gauges[metricName] = value;
                    _changedGauges[metricName] = value;
                }
            }
        }
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
        Dictionary<string, long> gauges;
        lock (_gaugesLock)
            gauges = new Dictionary<string, long>(_gauges);
        hadCounterActivity = false;

        foreach (var key in _bufferedCounters.Keys)
        {
            var value = _bufferedCounters.TryRemove(key, out var v) ? v : 0;
            if (value == 0) continue;

            counters[key] = value;
            hadCounterActivity = true;
        }

        return new MetricsSnapshot(counters, gauges);
    }

    /// <summary>
    /// Получает накопленные high-throughput counters и gauges, изменившиеся с прошлого flush.
    /// При отсутствии активности возвращает пустой снимок — idle-метрики не экспортируются.
    /// </summary>
    public MetricsSnapshot TakeBufferedSnapshot()
    {
        var counters = new Dictionary<string, long>();
        var gauges = new Dictionary<string, long>();

        foreach (var key in _bufferedCounters.Keys)
        {
            var value = _bufferedCounters.TryRemove(key, out var current) ? current : 0;
            if (value != 0) counters[key] = value;
        }

        lock (_gaugesLock)
        {
            foreach (var key in _changedGauges.Keys)
            {
                if (_changedGauges.TryRemove(key, out var value))
                    gauges[key] = value;
            }
        }

        return new MetricsSnapshot(counters, gauges);
    }

    public ChannelReader<MetricsSnapshot> ImmediateSnapshots => _immediateSnapshots.Reader;
}

public sealed record MetricsSnapshot(
    IReadOnlyDictionary<string, long> Counters,
    IReadOnlyDictionary<string, long> Gauges);

/// <summary>Определяет, какие counters должны агрегироваться перед экспортом в Seq.</summary>
public sealed class MetricsExportProfile
{
    private readonly bool _bufferAll;
    private readonly HashSet<string> _bufferedMetrics;

    private MetricsExportProfile(bool bufferAll, IEnumerable<string>? bufferedMetrics = null)
    {
        _bufferAll = bufferAll;
        _bufferedMetrics = bufferedMetrics?.ToHashSet(StringComparer.Ordinal) ?? [];
    }

    public static MetricsExportProfile BufferAll() => new(true);
    public static MetricsExportProfile ImmediateByDefault(params string[] bufferedMetrics) => new(false, bufferedMetrics);

    public bool IsBuffered(string metricName) =>
        _bufferAll || metricName.Contains("bytes", StringComparison.OrdinalIgnoreCase) || _bufferedMetrics.Contains(metricName);
}
