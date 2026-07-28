using Barkfluff.AdminPanel.Data;

namespace Barkfluff.AdminPanel.Services;

/// <summary>Builds idempotent hourly rollups from ServiceMetrics schema v2 events in Seq.</summary>
public class MetricsCollectorService : BackgroundService
{
    private static readonly TimeSpan CollectionInterval = TimeSpan.FromMinutes(5);
    private const int StatsHoursToKeep = 24;
    private const int ServiceMetricsHoursToKeep = 24 * 30;
    private const int RecoveryHours = 72;
    private const int RecoveryHoursPerCycle = 6;
    private const int MaxEventsPerHour = 100_000;

    private readonly IServiceProvider _serviceProvider;
    private readonly MetricsCacheDbContext _cache;
    private readonly ILogger<MetricsCollectorService> _logger;

    public MetricsCollectorService(IServiceProvider serviceProvider, MetricsCacheDbContext cache,
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
            var events = await seq.GetAllEventsListAsync(fromDateUtc: hour, toDateUtc: hour.AddHours(1), maxEvents: 10_000);
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
                if (service is not null) perService[service] = perService.GetValueOrDefault(service) + 1;
            }

            var stats = new HourlyStats { HourUtc = hour, TotalEvents = events.Count, ErrorCount = errors, WarningCount = warnings, PerService = perService };
            _cache.HourlyStats.Upsert(stats);
            _cache.HourlyTraffic.Upsert(new HourlyTraffic { HourUtc = hour, AllCount = stats.TotalEvents, ErrorCount = errors, WarningCount = warnings });
        }
    }

    private async Task CollectServiceMetricsAsync(SeqService seq, CancellationToken ct)
    {
        var currentHour = TruncateToHour(DateTime.UtcNow);
        await CollectServiceMetricHourAsync(seq, currentHour.AddHours(-1), ct);
        await CollectServiceMetricHourAsync(seq, currentHour, ct);

        var oldest = currentHour.AddHours(-RecoveryHours);
        var recoveryHours = Enumerable.Range(0, RecoveryHours)
            .Select(offset => oldest.AddHours(offset))
            .Where(hour => hour != currentHour && hour != currentHour.AddHours(-1))
            .Where(hour => _cache.MetricRollupHours.FindById(hour) is null)
            .Take(RecoveryHoursPerCycle);

        foreach (var hour in recoveryHours)
        {
            ct.ThrowIfCancellationRequested();
            await CollectServiceMetricHourAsync(seq, hour, ct);
        }
    }

    private async Task CollectServiceMetricHourAsync(SeqService seq, DateTime hour, CancellationToken ct)
    {
        var events = await seq.GetAllEventsListAsync(
            filter: "Metrics.SchemaVersion = 2",
            fromDateUtc: hour,
            maxEvents: MaxEventsPerHour,
            toDateUtc: hour.AddHours(1));
        if (events is null) return;
        if (events.Count >= MaxEventsPerHour)
        {
            _logger.LogWarning("MetricsCollector: {Hour:o} reached the {Limit} Seq event limit; retrying without replacing rollup", hour, MaxEventsPerHour);
            return;
        }

        var counters = new Dictionary<string, Dictionary<string, long>>(StringComparer.OrdinalIgnoreCase);
        var gauges = new Dictionary<string, Dictionary<string, (DateTime Timestamp, long Value)>>(StringComparer.OrdinalIgnoreCase);
        foreach (var evt in events)
        {
            if (!ServiceMetricsEventParser.TryParse(evt, out var snapshot)) continue;
            var service = MetricsCatalog.Find(snapshot.ServiceName);
            if (service is null) continue;

            var allowedCounters = service.Metrics.Where(metric => metric.Kind == "counter").Select(metric => metric.Id).ToHashSet(StringComparer.Ordinal);
            var allowedGauges = service.Metrics.Where(metric => metric.Kind == "gauge").Select(metric => metric.Id).ToHashSet(StringComparer.Ordinal);
            var serviceCounters = counters.GetValueOrDefault(service.Name) ?? (counters[service.Name] = new(StringComparer.Ordinal));
            var serviceGauges = gauges.GetValueOrDefault(service.Name) ?? (gauges[service.Name] = new(StringComparer.Ordinal));

            foreach (var (name, value) in snapshot.Counters)
                if (allowedCounters.Contains(name)) serviceCounters[name] = serviceCounters.GetValueOrDefault(name) + value;
            foreach (var (name, value) in snapshot.Gauges)
                if (allowedGauges.Contains(name) && (!serviceGauges.TryGetValue(name, out var current) || snapshot.Timestamp >= current.Timestamp))
                    serviceGauges[name] = (snapshot.Timestamp, value);
        }

        foreach (var service in MetricsCatalog.Services)
        {
            _cache.HourlyServiceMetrics.DeleteMany(row => row.HourUtc == hour && row.ServiceName == service.Name);
            counters.TryGetValue(service.Name, out var serviceCounters);
            gauges.TryGetValue(service.Name, out var serviceGauges);
            if (serviceCounters is null && serviceGauges is null)
                continue;

            _cache.HourlyServiceMetrics.Insert(new HourlyServiceMetrics
            {
                HourUtc = hour,
                ServiceName = service.Name,
                Counters = serviceCounters ?? new(),
                Gauges = serviceGauges?.ToDictionary(item => item.Key, item => item.Value.Value) ?? new(),
                SchemaVersion = 2
            });
        }

        _cache.MetricRollupHours.Upsert(new MetricRollupHour { HourUtc = hour, CompletedAtUtc = DateTime.UtcNow });
    }

    private void CleanupOldData()
    {
        var now = TruncateToHour(DateTime.UtcNow);
        _cache.HourlyStats.DeleteMany(x => x.HourUtc < now.AddHours(-StatsHoursToKeep));
        _cache.HourlyTraffic.DeleteMany(x => x.HourUtc < now.AddHours(-StatsHoursToKeep));
        _cache.HourlyServiceMetrics.DeleteMany(x => x.HourUtc < now.AddHours(-ServiceMetricsHoursToKeep));
        _cache.MetricRollupHours.DeleteMany(x => x.HourUtc < now.AddHours(-ServiceMetricsHoursToKeep));
    }

    private static DateTime TruncateToHour(DateTime value) => new(value.Year, value.Month, value.Day, value.Hour, 0, 0, DateTimeKind.Utc);

    private static string? GetEventLevel(System.Text.Json.JsonElement evt) =>
        evt.TryGetProperty("Level", out var level) && level.ValueKind == System.Text.Json.JsonValueKind.String ? level.GetString() : null;

    private static string? GetEventApplication(System.Text.Json.JsonElement evt)
    {
        if (!evt.TryGetProperty("Properties", out var props)) return null;
        if (props.ValueKind == System.Text.Json.JsonValueKind.Object)
            return props.TryGetProperty("Application", out var value) && value.ValueKind == System.Text.Json.JsonValueKind.String ? value.GetString() : null;
        if (props.ValueKind != System.Text.Json.JsonValueKind.Array) return null;
        foreach (var property in props.EnumerateArray())
            if (property.TryGetProperty("Name", out var name) && name.GetString() == "Application" &&
                property.TryGetProperty("Value", out var value) && value.ValueKind == System.Text.Json.JsonValueKind.String)
                return value.GetString();
        return null;
    }
}
