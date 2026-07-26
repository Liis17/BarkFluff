using Barkfluff.AdminPanel.Data;

using System.Text.Json;

namespace Barkfluff.AdminPanel.Services;

/// <summary>
/// Builds the dashboard's hourly metric rollups from the versioned ServiceMetrics Seq events.
/// Counters are summed within an hour while gauges retain their latest value.
/// </summary>
public class MetricsCollectorService : BackgroundService
{
    private static readonly TimeSpan CollectionInterval = TimeSpan.FromMinutes(5);
    private const int StatsHoursToKeep = 24;
    private const int ServiceMetricsHoursToKeep = 24 * 30;
    private const int MaxEventsPerServiceHour = 100_000;

    private readonly IServiceProvider _serviceProvider;
    private readonly MetricsCacheDbContext _cache;
    private readonly ILogger<MetricsCollectorService> _logger;

    public MetricsCollectorService(
        IServiceProvider serviceProvider,
        MetricsCacheDbContext cache,
        ILogger<MetricsCollectorService> logger)
    {
        _serviceProvider = serviceProvider;
        _cache = cache;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        await CollectAllAsync(stoppingToken);

        using var timer = new PeriodicTimer(CollectionInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
            await CollectAllAsync(stoppingToken);
    }

    private async Task CollectAllAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var seq = scope.ServiceProvider.GetRequiredService<SeqService>();

            await CollectLogTrafficAsync(seq, ct);
            await CollectServiceMetricsAsync(seq, ct);
            CleanupOldData();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MetricsCollector: collection failed");
        }
    }

    private async Task CollectLogTrafficAsync(SeqService seq, CancellationToken ct)
    {
        var currentHour = TruncateToHour(DateTime.UtcNow);
        foreach (var hour in new[] { currentHour.AddHours(-1), currentHour })
        {
            ct.ThrowIfCancellationRequested();
            var events = await seq.GetAllEventsListAsync(
                fromDateUtc: hour,
                toDateUtc: hour.AddHours(1),
                maxEvents: 10_000);
            if (events is null) continue;

            var perService = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            long errors = 0;
            long warnings = 0;
            foreach (var evt in events)
            {
                var level = GetEventLevel(evt);
                if (level is "Error" or "Fatal") errors++;
                if (level == "Warning") warnings++;
                var service = GetEventApplication(evt);
                if (service is not null)
                    perService[service] = perService.GetValueOrDefault(service) + 1;
            }

            var stats = new HourlyStats
            {
                HourUtc = hour,
                TotalEvents = events.Count,
                ErrorCount = errors,
                WarningCount = warnings,
                PerService = perService
            };
            _cache.HourlyStats.Upsert(stats);
            _cache.HourlyTraffic.Upsert(new HourlyTraffic
            {
                HourUtc = hour,
                AllCount = stats.TotalEvents,
                ErrorCount = errors,
                WarningCount = warnings
            });
        }
    }

    private async Task CollectServiceMetricsAsync(SeqService seq, CancellationToken ct)
    {
        var currentHour = TruncateToHour(DateTime.UtcNow);
        // Recompute the current and previous hour so late Seq ingestion cannot produce a stale rollup.
        foreach (var hour in new[] { currentHour.AddHours(-1), currentHour })
        {
            foreach (var service in MetricsCatalog.Services)
            {
                ct.ThrowIfCancellationRequested();
                var filter = $"Application = '{service.Name.Replace("'", "''")}' and @Message like 'ServiceMetrics%'";
                var events = await seq.GetAllEventsListAsync(
                    filter, hour, MaxEventsPerServiceHour, hour.AddHours(1));
                if (events is null) continue;

                var counters = new Dictionary<string, long>(StringComparer.Ordinal);
                var gauges = new Dictionary<string, (DateTime Timestamp, long Value)>(StringComparer.Ordinal);
                foreach (var evt in events)
                {
                    var snapshot = ExtractSnapshot(evt);
                    if (snapshot is null) continue;

                    foreach (var (name, value) in snapshot.Counters)
                        counters[name] = counters.GetValueOrDefault(name) + value;

                    foreach (var (name, value) in snapshot.Gauges)
                    {
                        if (!gauges.TryGetValue(name, out var current) || snapshot.Timestamp >= current.Timestamp)
                            gauges[name] = (snapshot.Timestamp, value);
                    }
                }

                _cache.HourlyServiceMetrics.DeleteMany(x => x.HourUtc == hour && x.ServiceName == service.Name);
                if (counters.Count == 0 && gauges.Count == 0) continue;

                _cache.HourlyServiceMetrics.Insert(new HourlyServiceMetrics
                {
                    HourUtc = hour,
                    ServiceName = service.Name,
                    Counters = counters,
                    Gauges = gauges.ToDictionary(x => x.Key, x => x.Value.Value),
                    SchemaVersion = 2
                });
            }
        }
    }

    private void CleanupOldData()
    {
        var now = TruncateToHour(DateTime.UtcNow);
        _cache.HourlyStats.DeleteMany(x => x.HourUtc < now.AddHours(-StatsHoursToKeep));
        _cache.HourlyTraffic.DeleteMany(x => x.HourUtc < now.AddHours(-StatsHoursToKeep));
        _cache.HourlyServiceMetrics.DeleteMany(x => x.HourUtc < now.AddHours(-ServiceMetricsHoursToKeep));
    }

    private static DateTime TruncateToHour(DateTime value) =>
        new(value.Year, value.Month, value.Day, value.Hour, 0, 0, DateTimeKind.Utc);

    private static MetricSnapshot? ExtractSnapshot(JsonElement evt)
    {
        if (!evt.TryGetProperty("Properties", out var props)) return null;
        var wrapper = GetProperty(props, "Metrics");
        if (wrapper is null || wrapper.Value.ValueKind != JsonValueKind.Object) return null;

        var metrics = wrapper.Value;
        if (metrics.TryGetProperty("Metrics", out var nested) && nested.ValueKind == JsonValueKind.Object)
            metrics = nested;
        if (!metrics.TryGetProperty("SchemaVersion", out var version) || version.GetInt32() != 2)
            return null;

        var timestamp = GetEventTimestamp(evt) ?? DateTime.MinValue;
        return new MetricSnapshot(
            ExtractValues(metrics, "Counters"),
            ExtractValues(metrics, "Gauges"),
            timestamp);
    }

    private static Dictionary<string, long> ExtractValues(JsonElement source, string property)
    {
        var values = new Dictionary<string, long>(StringComparer.Ordinal);
        if (!source.TryGetProperty(property, out var objectValue) || objectValue.ValueKind != JsonValueKind.Object)
            return values;
        foreach (var item in objectValue.EnumerateObject())
            if (item.Value.ValueKind == JsonValueKind.Number && item.Value.TryGetInt64(out var value))
                values[item.Name] = value;
        return values;
    }

    private static string? GetEventLevel(JsonElement evt) =>
        evt.TryGetProperty("Level", out var level) && level.ValueKind == JsonValueKind.String ? level.GetString() : null;

    private static DateTime? GetEventTimestamp(JsonElement evt) =>
        evt.TryGetProperty("Timestamp", out var timestamp) && timestamp.ValueKind == JsonValueKind.String &&
        DateTime.TryParse(timestamp.GetString(), out var value) ? value.ToUniversalTime() : null;

    private static string? GetEventApplication(JsonElement evt)
    {
        if (!evt.TryGetProperty("Properties", out var props)) return null;
        var app = GetProperty(props, "Application");
        return app?.ValueKind == JsonValueKind.String ? app.Value.GetString() : null;
    }

    private static JsonElement? GetProperty(JsonElement props, string name)
    {
        if (props.ValueKind == JsonValueKind.Object)
            return props.TryGetProperty(name, out var value) ? value : null;
        if (props.ValueKind != JsonValueKind.Array) return null;
        foreach (var property in props.EnumerateArray())
            if (property.TryGetProperty("Name", out var propertyName) && propertyName.GetString() == name &&
                property.TryGetProperty("Value", out var value))
                return value;
        return null;
    }

    private sealed record MetricSnapshot(
        Dictionary<string, long> Counters,
        Dictionary<string, long> Gauges,
        DateTime Timestamp);
}
