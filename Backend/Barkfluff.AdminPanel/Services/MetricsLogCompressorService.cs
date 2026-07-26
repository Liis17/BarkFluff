using Barkfluff.AdminPanel.Data;
using Barkfluff.AdminPanel.Models;

using Microsoft.Extensions.Options;

using System.Text.Json;

namespace Barkfluff.AdminPanel.Services;

public class MetricsLogCompressorService : BackgroundService
{
    private readonly IServiceProvider _sp;
    private readonly IOptions<LogsCompressionSettings> _settings;
    private readonly ILogger<MetricsLogCompressorService> _logger;

    public MetricsLogCompressorService(
        IServiceProvider sp,
        IOptions<LogsCompressionSettings> settings,
        ILogger<MetricsLogCompressorService> logger)
    {
        _sp = sp;
        _settings = settings;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = TimeUntilNextRun(DateTime.UtcNow);
            _logger.LogInformation(
                "MetricsCompressor: next run in {Delay} at {NextRunUtc:o}",
                delay, DateTime.UtcNow.Add(delay));

            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (!_settings.Value.Enabled)
            {
                _logger.LogInformation("MetricsCompressor: disabled via configuration, skipping run");
                continue;
            }

            var dayToCompress = DateTime.UtcNow.Date.AddDays(-1);
            try
            {
                await CompressDayAsync(dayToCompress, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MetricsCompressor: scheduled run for {Day:yyyy-MM-dd} failed", dayToCompress);
            }
        }
    }

    public async Task<MetricsCompressionRun?> CompressDayAsync(DateTime dayUtc, CancellationToken ct)
    {
        var dayStart = new DateTime(dayUtc.Year, dayUtc.Month, dayUtc.Day, 0, 0, 0, DateTimeKind.Utc);
        var dayEnd = dayStart.AddDays(1);

        using var scope = _sp.CreateScope();
        var cache = scope.ServiceProvider.GetRequiredService<MetricsCacheDbContext>();
        var seq = scope.ServiceProvider.GetRequiredService<SeqService>();

        var existing = cache.CompressionRuns.FindById(dayStart);
        if (existing != null && !existing.DryRun)
        {
            _logger.LogInformation(
                "MetricsCompressor: compression for {Day:yyyy-MM-dd} already done at {CompletedAtUtc:o}, skipping",
                dayStart, existing.CompletedAtUtc);
            return existing;
        }

        var settings = _settings.Value;
        _logger.LogInformation(
            "MetricsCompressor: starting compression for {Day:yyyy-MM-dd} (DryRun={DryRun})",
            dayStart, settings.DryRun);

        // 1. Fetch all ServiceMetrics events for the day
        var filterRead = "@Message like 'ServiceMetrics %'";
        var events = await seq.GetAllEventsListAsync(
            filter: filterRead,
            fromDateUtc: dayStart,
            maxEvents: settings.MaxEventsPerRun,
            toDateUtc: dayEnd);

        if (events == null)
        {
            _logger.LogWarning(
                "MetricsCompressor: failed to fetch events from Seq for {Day:yyyy-MM-dd}",
                dayStart);
            return null;
        }

        if (events.Count == 0)
        {
            _logger.LogInformation(
                "MetricsCompressor: no ServiceMetrics events for {Day:yyyy-MM-dd}, nothing to do",
                dayStart);

            var emptyRun = new MetricsCompressionRun
            {
                DayUtc = dayStart,
                CompletedAtUtc = DateTime.UtcNow,
                ServiceCount = 0,
                SourceEventCount = 0,
                DeletedCount = 0,
                DryRun = settings.DryRun
            };
            cache.CompressionRuns.Upsert(emptyRun);
            return emptyRun;
        }

        // 2. Group by service, aggregate metrics
        var perService = AggregateByService(events);

        // The dashboard cache is the only source of hourly history after raw Seq events
        // are removed. Do not compress away an hour that was not successfully rolled up.
        if (!HasHourlyRollups(cache, events))
        {
            _logger.LogWarning(
                "MetricsCompressor: hourly dashboard rollups for {Day:yyyy-MM-dd} are incomplete; keeping raw events",
                dayStart);
            return null;
        }

        // 3. Write one summary event per service directly into Seq (CLEF ingest)
        var summaryTemplate = $"{settings.SummaryMessagePrefix} {{ServiceName}} {{Date}} {{EventCount}} {{@Aggregated}}";
        var summaryTimestamp = dayEnd.AddSeconds(-1);

        foreach (var (serviceName, agg) in perService)
        {
            var aggregatedDto = agg.Metrics.ToDictionary(
                kv => kv.Key,
                kv => (object)new
                {
                    sum = kv.Value.Sum,
                    avg = kv.Value.Avg,
                    min = kv.Value.Min,
                    max = kv.Value.Max,
                    last = kv.Value.Last,
                    count = kv.Value.Count
                });

            var properties = new Dictionary<string, object?>
            {
                ["Application"] = serviceName,
                ["ServiceName"] = serviceName,
                ["Date"] = dayStart.ToString("yyyy-MM-dd"),
                ["EventCount"] = agg.EventCount,
                ["Aggregated"] = aggregatedDto,
                ["SourceLogType"] = settings.SourceMessageTemplate,
                ["SummaryProducedBy"] = "Barkfluff.AdminPanel.MetricsLogCompressor"
            };

            await seq.IngestEventAsync(
                timestampUtc: summaryTimestamp,
                level: "Information",
                messageTemplate: summaryTemplate,
                properties: properties);
        }

        // 4. Delete original events (precise template match to avoid touching summaries)
        int deletedCount = 0;
        if (!settings.DryRun)
        {
            var filterDelete = $"@MessageTemplate = '{EscapeSeqString(settings.SourceMessageTemplate)}'";
            try
            {
                await seq.DeleteEventsAsync(
                    filter: filterDelete,
                    fromDateUtc: dayStart,
                    toDateUtc: dayEnd);
                deletedCount = events.Count;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "MetricsCompressor: delete stage failed for {Day:yyyy-MM-dd} — summaries already written, originals NOT deleted",
                    dayStart);
                throw;
            }
        }

        var run = new MetricsCompressionRun
        {
            DayUtc = dayStart,
            CompletedAtUtc = DateTime.UtcNow,
            ServiceCount = perService.Count,
            SourceEventCount = events.Count,
            DeletedCount = deletedCount,
            DryRun = settings.DryRun
        };
        cache.CompressionRuns.Upsert(run);

        _logger.LogInformation(
            "MetricsCompressor: done for {Day:yyyy-MM-dd}: services={ServiceCount}, source={SourceCount}, deleted={DeletedCount}, dryRun={DryRun}",
            dayStart, run.ServiceCount, run.SourceEventCount, run.DeletedCount, run.DryRun);

        return run;
    }

    private TimeSpan TimeUntilNextRun(DateTime nowUtc)
    {
        var s = _settings.Value;
        var todayRun = new DateTime(
            nowUtc.Year, nowUtc.Month, nowUtc.Day,
            s.ScheduleUtcHour, s.ScheduleUtcMinute, 0,
            DateTimeKind.Utc);
        var next = nowUtc < todayRun ? todayRun : todayRun.AddDays(1);
        var delay = next - nowUtc;
        return delay < TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : delay;
    }

    private static Dictionary<string, ServiceAggregate> AggregateByService(List<JsonElement> events)
    {
        var perService = new Dictionary<string, ServiceAggregate>(StringComparer.OrdinalIgnoreCase);

        foreach (var evt in events)
        {
            var serviceName = GetEventApplication(evt);
            if (string.IsNullOrEmpty(serviceName)) continue;

            var ts = GetEventTimestamp(evt);
            var values = ExtractMetricValues(evt);
            if (values.Count == 0) continue;

            if (!perService.TryGetValue(serviceName, out var agg))
            {
                agg = new ServiceAggregate();
                perService[serviceName] = agg;
            }

            agg.EventCount++;
            foreach (var (name, value) in values)
            {
                if (!agg.Metrics.TryGetValue(name, out var stats))
                {
                    stats = new MetricStats
                    {
                        Sum = 0,
                        Min = long.MaxValue,
                        Max = long.MinValue,
                        Count = 0,
                        Last = value,
                        LastTimestamp = ts ?? DateTime.MinValue
                    };
                    agg.Metrics[name] = stats;
                }

                stats.Sum += value;
                stats.Count++;
                if (value < stats.Min) stats.Min = value;
                if (value > stats.Max) stats.Max = value;
                if (ts.HasValue && ts.Value >= stats.LastTimestamp)
                {
                    stats.Last = value;
                    stats.LastTimestamp = ts.Value;
                }
            }
        }

        return perService;
    }

    private static string EscapeSeqString(string s) => s.Replace("'", "''");

    private static bool HasHourlyRollups(MetricsCacheDbContext cache, IEnumerable<JsonElement> events)
    {
        var required = events
            .Select(e => (Service: GetEventApplication(e), Timestamp: GetEventTimestamp(e)))
            .Where(x => !string.IsNullOrWhiteSpace(x.Service) && x.Timestamp.HasValue)
            .Select(x => (x.Service!, Hour: TruncateToHour(x.Timestamp!.Value)))
            .Distinct()
            .ToList();

        return required.All(x => cache.HourlyServiceMetrics.FindOne(row =>
            row.ServiceName == x.Item1 && row.HourUtc == x.Hour && row.SchemaVersion == 2) is not null);
    }

    private static DateTime TruncateToHour(DateTime value) =>
        new(value.Year, value.Month, value.Day, value.Hour, 0, 0, DateTimeKind.Utc);

    #region JsonElement helpers (mirror of MetricsCollectorService)

    private static string? GetEventApplication(JsonElement evt)
    {
        if (evt.ValueKind != JsonValueKind.Object) return null;
        if (!evt.TryGetProperty("Properties", out var props)) return null;
        var app = GetPropertyFromSeqProps(props, "Application");
        return app?.ValueKind == JsonValueKind.String ? app.Value.GetString() : null;
    }

    private static DateTime? GetEventTimestamp(JsonElement evt)
    {
        if (evt.ValueKind != JsonValueKind.Object) return null;
        if (!evt.TryGetProperty("Timestamp", out var ts) || ts.ValueKind != JsonValueKind.String)
            return null;
        return DateTime.TryParse(ts.GetString(), null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt)
            ? dt
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

    private static Dictionary<string, long> ExtractMetricValues(JsonElement evt)
    {
        var metrics = new Dictionary<string, long>();
        if (evt.ValueKind != JsonValueKind.Object) return metrics;
        if (!evt.TryGetProperty("Properties", out var props)) return metrics;

        var metricsVal = GetPropertyFromSeqProps(props, "Metrics");
        if (metricsVal == null || metricsVal.Value.ValueKind != JsonValueKind.Object)
            return metrics;

        var metricsWrapper = metricsVal.Value;
        JsonElement metricsObj;
        if (metricsWrapper.TryGetProperty("Metrics", out var innerMetrics)
            && innerMetrics.ValueKind == JsonValueKind.Object)
        {
            metricsObj = innerMetrics;
        }
        else
        {
            metricsObj = metricsWrapper;
        }

        // Schema v2 exposes the type explicitly. Keep the namespaces separate in the
        // daily archive so a gauge can never be mistaken for a summed counter.
        if (metricsObj.TryGetProperty("SchemaVersion", out var schemaVersion)
            && schemaVersion.ValueKind == JsonValueKind.Number
            && schemaVersion.TryGetInt32(out var version)
            && version == 2)
        {
            AddTypedValues(metrics, metricsObj, "Counters", "counter.");
            AddTypedValues(metrics, metricsObj, "Gauges", "gauge.");
            return metrics;
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

    private static void AddTypedValues(Dictionary<string, long> target, JsonElement source, string property, string prefix)
    {
        if (!source.TryGetProperty(property, out var values) || values.ValueKind != JsonValueKind.Object)
            return;
        foreach (var value in values.EnumerateObject())
            if (value.Value.ValueKind == JsonValueKind.Number && value.Value.TryGetInt64(out var number))
                target[prefix + value.Name] = number;
    }

    #endregion
}

public class ServiceAggregate
{
    public int EventCount { get; set; }
    public Dictionary<string, MetricStats> Metrics { get; } = new(StringComparer.OrdinalIgnoreCase);
}

public class MetricStats
{
    public long Sum { get; set; }
    public long Min { get; set; }
    public long Max { get; set; }
    public long Count { get; set; }
    public long Last { get; set; }
    public DateTime LastTimestamp { get; set; }
    public double Avg => Count > 0 ? (double)Sum / Count : 0;
}
