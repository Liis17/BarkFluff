using System.Text.Json;
using Barkfluff.AdminPanel.Data;

namespace Barkfluff.AdminPanel.Services;

public class MetricsCollectorService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly MetricsCacheDbContext _cache;
    private readonly ILogger<MetricsCollectorService> _logger;

    private static readonly TimeSpan CollectionInterval = TimeSpan.FromHours(1);
    private const int StatsHoursToKeep = 24;
    private const int ServiceMetricsHoursToKeep = 12;
    private const int MaxEventsPerHour = 10000;

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
        // Initial collection on startup (small delay to let services initialize)
        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        await CollectAllAsync(stoppingToken);

        // Then run every hour
        using var timer = new PeriodicTimer(CollectionInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await CollectAllAsync(stoppingToken);
        }
    }

    private async Task CollectAllAsync(CancellationToken ct)
    {
        try
        {
            _logger.LogInformation("MetricsCollector: starting hourly collection");

            using var scope = _serviceProvider.CreateScope();
            var seqService = scope.ServiceProvider.GetRequiredService<SeqService>();

            await CollectHourlyStatsAndTrafficAsync(seqService, ct);
            await CollectServiceMetricsAsync(seqService, ct);
            CleanupOldData();

            _logger.LogInformation("MetricsCollector: collection completed");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Shutting down
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MetricsCollector: error during collection");
        }
    }

    private async Task CollectHourlyStatsAndTrafficAsync(SeqService seqService, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var currentHour = TruncateToHour(now);

        for (int i = StatsHoursToKeep - 1; i >= 0; i--)
        {
            ct.ThrowIfCancellationRequested();

            var hourStart = currentHour.AddHours(-i);
            var hourEnd = hourStart.AddHours(1);
            var isCurrentHour = hourStart == currentHour;

            // Skip completed past hours that already exist in cache
            if (!isCurrentHour)
            {
                var existingStats = _cache.HourlyStats.FindById(hourStart);
                if (existingStats != null)
                    continue;
            }

            _logger.LogDebug("MetricsCollector: fetching hour {Hour}", hourStart);

            var events = await seqService.GetAllEventsListAsync(
                filter: null,
                fromDateUtc: hourStart,
                maxEvents: MaxEventsPerHour,
                toDateUtc: hourEnd);

            if (events == null)
            {
                _logger.LogWarning("MetricsCollector: failed to fetch events for hour {Hour}", hourStart);
                continue;
            }

            // Aggregate stats
            long totalEvents = events.Count;
            long errorCount = 0;
            long warningCount = 0;
            var perService = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

            foreach (var evt in events)
            {
                var level = GetEventLevel(evt);
                if (level is "Error" or "Fatal") errorCount++;
                if (level == "Warning") warningCount++;

                var app = GetEventApplication(evt);
                if (!string.IsNullOrEmpty(app))
                {
                    perService[app] = perService.GetValueOrDefault(app) + 1;
                }
            }

            // Upsert hourly stats
            _cache.HourlyStats.Upsert(new HourlyStats
            {
                HourUtc = hourStart,
                TotalEvents = totalEvents,
                ErrorCount = errorCount,
                WarningCount = warningCount,
                PerService = perService
            });

            // Upsert hourly traffic
            _cache.HourlyTraffic.Upsert(new HourlyTraffic
            {
                HourUtc = hourStart,
                AllCount = totalEvents,
                ErrorCount = errorCount,
                WarningCount = warningCount
            });
        }
    }

    private async Task CollectServiceMetricsAsync(SeqService seqService, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var currentHour = TruncateToHour(now);

        for (int i = ServiceMetricsHoursToKeep - 1; i >= 0; i--)
        {
            ct.ThrowIfCancellationRequested();

            var hourStart = currentHour.AddHours(-i);
            var hourEnd = hourStart.AddHours(1);
            var isCurrentHour = hourStart == currentHour;

            // Skip completed past hours that already exist in cache
            if (!isCurrentHour)
            {
                var existing = _cache.HourlyServiceMetrics.Find(x => x.HourUtc == hourStart).Any();
                if (existing)
                    continue;
            }

            var filter = "@Message like 'ServiceMetrics%'";
            var events = await seqService.GetAllEventsListAsync(
                filter: filter,
                fromDateUtc: hourStart,
                maxEvents: 1000,
                toDateUtc: hourEnd);

            if (events == null || events.Count == 0)
                continue;

            // Group by service, pick latest metrics per service for this hour
            var serviceMetrics = new Dictionary<string, Dictionary<string, long>>(StringComparer.OrdinalIgnoreCase);

            foreach (var evt in events)
            {
                var serviceName = GetEventApplication(evt);
                if (string.IsNullOrEmpty(serviceName)) continue;

                var metrics = ExtractMetricValues(evt);
                if (metrics.Count == 0) continue;

                // Overwrite with latest (events are ordered)
                serviceMetrics[serviceName] = metrics;
            }

            // Delete existing entries for this hour (for re-fetch of current hour)
            if (isCurrentHour)
            {
                _cache.HourlyServiceMetrics.DeleteMany(x => x.HourUtc == hourStart);
            }

            // Insert per-service entries
            foreach (var (serviceName, metrics) in serviceMetrics)
            {
                _cache.HourlyServiceMetrics.Insert(new HourlyServiceMetrics
                {
                    HourUtc = hourStart,
                    ServiceName = serviceName,
                    Metrics = metrics
                });
            }
        }
    }

    private void CleanupOldData()
    {
        var cutoffStats = TruncateToHour(DateTime.UtcNow).AddHours(-StatsHoursToKeep - 1);
        var cutoffMetrics = TruncateToHour(DateTime.UtcNow).AddHours(-ServiceMetricsHoursToKeep - 1);

        _cache.HourlyStats.DeleteMany(x => x.HourUtc < cutoffStats);
        _cache.HourlyTraffic.DeleteMany(x => x.HourUtc < cutoffStats);
        _cache.HourlyServiceMetrics.DeleteMany(x => x.HourUtc < cutoffMetrics);
    }

    private static DateTime TruncateToHour(DateTime dt)
    {
        return new DateTime(dt.Year, dt.Month, dt.Day, dt.Hour, 0, 0, DateTimeKind.Utc);
    }

    private static string? GetEventLevel(JsonElement evt)
    {
        if (evt.ValueKind != JsonValueKind.Object) return null;
        return evt.TryGetProperty("Level", out var level) && level.ValueKind == JsonValueKind.String
            ? level.GetString()
            : null;
    }

    private static JsonElement? GetPropertyFromSeqProps(JsonElement props, string name)
    {
        if (props.ValueKind == JsonValueKind.Object)
        {
            return props.TryGetProperty(name, out var val) ? val : null;
        }
        if (props.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in props.EnumerateArray())
            {
                if (item.TryGetProperty("Name", out var n) && n.GetString() == name
                    && item.TryGetProperty("Value", out var v))
                    return v;
            }
        }
        return null;
    }

    private static string? GetEventApplication(JsonElement evt)
    {
        if (evt.ValueKind != JsonValueKind.Object) return null;
        if (!evt.TryGetProperty("Properties", out var props)) return null;
        var app = GetPropertyFromSeqProps(props, "Application");
        return app?.ValueKind == JsonValueKind.String ? app.Value.GetString() : null;
    }

    private static Dictionary<string, long> ExtractMetricValues(JsonElement evt)
    {
        var metrics = new Dictionary<string, long>();
        if (evt.ValueKind != JsonValueKind.Object) return metrics;

        if (!evt.TryGetProperty("Properties", out var props)) return metrics;

        var metricsVal = GetPropertyFromSeqProps(props, "Metrics");
        if (metricsVal == null || metricsVal.Value.ValueKind != JsonValueKind.Object)
            return metrics;

        var metricsWrapper = metricsVal.Value;

        // Try nested structure: Metrics.Metrics
        JsonElement metricsObj;
        if (metricsWrapper.TryGetProperty("Metrics", out var innerMetrics) && innerMetrics.ValueKind == JsonValueKind.Object)
        {
            metricsObj = innerMetrics;
        }
        else
        {
            metricsObj = metricsWrapper;
        }

        foreach (var prop in metricsObj.EnumerateObject())
        {
            if (prop.Name is "ServiceName" or "Timestamp") continue;

            if (prop.Value.ValueKind == JsonValueKind.Number && prop.Value.TryGetInt64(out var val))
            {
                metrics[prop.Name] = val;
            }
        }

        return metrics;
    }
}
