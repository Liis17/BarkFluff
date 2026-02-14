using Barkfluff.AdminPanel.Models;
using Barkfluff.AdminPanel.Services;
using System.Text.Json;

namespace Barkfluff.AdminPanel.Endpoints;

public static class SeqEndpoints
{
    private static readonly string[] KnownServices =
    [
        "BarkFluff.Identity",
        "BarkFluff.Users",
        "BarkFluff.Messages",
        "BarkFluff.Files",
        "BarkFluff.Updates",
        "BarkFluff.Notification",
        "BarkFluff.Beacon",
        "BarkFluff.FastAuth",
        "BarkFluff.Onliner",
        "BarkFluff.Configuration"
    ];

    public static void MapSeqEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/seq")
            .WithName("Seq")
            .WithTags("Seq");

        group.MapGet("/events", async (
            SeqService seqService,
            HttpContext context,
            string? application,
            int count = 50,
            string? fromUtc = null,
            string? level = null,
            string? search = null,
            string? afterId = null) =>
        {
            if (context.Items["AuthToken"] is not AuthToken)
                return Results.Unauthorized();

            var filterParts = new List<string>();

            if (!string.IsNullOrEmpty(application))
            {
                filterParts.Add($"Application = '{application.Replace("'", "''")}'");
            }

            if (!string.IsNullOrEmpty(level))
            {
                if (level.Equals("Error", StringComparison.OrdinalIgnoreCase))
                {
                    filterParts.Add("@Level in ['Error', 'Fatal']");
                }
                else
                {
                    filterParts.Add($"@Level = '{level.Replace("'", "''")}'");
                }
            }

            if (!string.IsNullOrEmpty(search))
            {
                // Sanitize search input - escape single quotes and remove SQL wildcards
                var sanitized = search
                    .Replace("'", "''")
                    .Replace("%", "")
                    .Replace("_", "");
                filterParts.Add($"@Message like '%{sanitized}%'");
            }

            var filter = filterParts.Count > 0
                ? string.Join(" and ", filterParts)
                : null;

            DateTime? fromDate = null;
            if (!string.IsNullOrEmpty(fromUtc) && DateTime.TryParse(fromUtc, out var parsed))
                fromDate = parsed;

            var events = await seqService.GetEventsAsync(filter, count, fromDate, afterId);
            return events.HasValue ? Results.Ok(events.Value) : Results.StatusCode(502);
        })
        .WithName("GetSeqEvents")
        .WithOpenApi();

        group.MapGet("/services", (HttpContext context) =>
        {
            if (context.Items["AuthToken"] is not AuthToken)
                return Results.Unauthorized();

            return Results.Ok(KnownServices);
        })
        .WithName("GetSeqServices")
        .WithOpenApi();

        group.MapGet("/dashboard/kpis", async (
            SeqService seqService,
            HttpContext context,
            int hours = 24) =>
        {
            if (context.Items["AuthToken"] is not AuthToken)
                return Results.Unauthorized();

            var fromDateUtc = DateTime.UtcNow.AddHours(-hours);

            var events = await seqService.GetAllEventsListAsync(null, fromDateUtc, 5000);
            if (events == null)
                return Results.StatusCode(502);

            long totalCount = events.Count;
            long errorCount = events.Count(e => GetEventLevel(e) is "Error" or "Fatal");
            long warningCount = events.Count(e => GetEventLevel(e) == "Warning");
            var perService = events
                .Select(GetEventApplication)
                .Where(a => !string.IsNullOrEmpty(a))
                .GroupBy(a => a!)
                .ToDictionary(g => g.Key, g => (long)g.Count());

            return Results.Ok(new
            {
                totalEvents = totalCount,
                errorCount,
                warningCount,
                perService,
                periodHours = hours
            });
        })
        .WithName("GetDashboardKpis")
        .WithOpenApi();

        group.MapGet("/dashboard/traffic", async (
            SeqService seqService,
            HttpContext context,
            int hours = 24,
            string interval = "1h") =>
        {
            if (context.Items["AuthToken"] is not AuthToken)
                return Results.Unauthorized();

            var fromDateUtc = DateTime.UtcNow.AddHours(-hours);

            var events = await seqService.GetAllEventsListAsync(null, fromDateUtc, 5000);
            if (events == null)
                return Results.StatusCode(502);

            var intervalMinutes = ParseIntervalMinutes(interval);

            // Bucket events by time
            var allBuckets = new Dictionary<DateTime, long>();
            var errorBuckets = new Dictionary<DateTime, long>();

            foreach (var evt in events)
            {
                var ts = GetEventTimestamp(evt);
                if (!ts.HasValue) continue;

                var bucket = TruncateToInterval(ts.Value, intervalMinutes);
                allBuckets[bucket] = allBuckets.GetValueOrDefault(bucket) + 1;

                var level = GetEventLevel(evt);
                if (level is "Error" or "Fatal")
                    errorBuckets[bucket] = errorBuckets.GetValueOrDefault(bucket) + 1;
            }

            // Generate continuous time series with all buckets filled
            var allData = new List<object>();
            var errorsData = new List<object>();
            var bucketTime = TruncateToInterval(fromDateUtc, intervalMinutes);
            var now = DateTime.UtcNow;

            while (bucketTime <= now)
            {
                allData.Add(new { timestamp = bucketTime.ToString("o"), count = allBuckets.GetValueOrDefault(bucketTime) });
                errorsData.Add(new { timestamp = bucketTime.ToString("o"), count = errorBuckets.GetValueOrDefault(bucketTime) });
                bucketTime = bucketTime.AddMinutes(intervalMinutes);
            }

            return Results.Ok(new
            {
                all = allData,
                errors = errorsData
            });
        })
        .WithName("GetDashboardTraffic")
        .WithOpenApi();

        group.MapGet("/dashboard/metrics", async (
            SeqService seqService,
            HttpContext context,
            int hours = 1) =>
        {
            if (context.Items["AuthToken"] is not AuthToken)
                return Results.Unauthorized();

            try
            {
                var fromDateUtc = DateTime.UtcNow.AddHours(-hours);

                var filter = "@Message like 'ServiceMetrics%'";
                var eventsResult = await seqService.GetEventsAsync(filter, 200, fromDateUtc);

                if (eventsResult == null)
                    return Results.StatusCode(502);

                var services = ExtractServiceMetricsFromEvents(eventsResult.Value);

                return Results.Ok(new
                {
                    periodHours = hours,
                    services
                });
            }
            catch (Exception ex)
            {
                return Results.Problem(
                    detail: ex.Message,
                    statusCode: 500,
                    title: "Failed to extract metrics");
            }
        })
        .WithName("GetDashboardMetrics")
        .WithOpenApi();

        group.MapGet("/services/status", async (
            SeqService seqService,
            HttpContext context,
            int hours = 24) =>
        {
            if (context.Items["AuthToken"] is not AuthToken)
                return Results.Unauthorized();

            var fromDateUtc = DateTime.UtcNow.AddHours(-hours);

            var events = await seqService.GetAllEventsListAsync(null, fromDateUtc, 5000);
            if (events == null)
                return Results.StatusCode(502);

            // Aggregate per-service stats
            var serviceData = new Dictionary<string, (long eventCount, long errorCount, DateTime? lastSeen)>(StringComparer.OrdinalIgnoreCase);

            foreach (var evt in events)
            {
                var app = GetEventApplication(evt);
                if (string.IsNullOrEmpty(app)) continue;

                var ts = GetEventTimestamp(evt);
                var level = GetEventLevel(evt);

                if (!serviceData.TryGetValue(app, out var data))
                    data = (0, 0, null);

                data.eventCount++;
                if (level is "Error" or "Fatal") data.errorCount++;
                if (ts.HasValue && (!data.lastSeen.HasValue || ts.Value > data.lastSeen.Value))
                    data.lastSeen = ts.Value;

                serviceData[app] = data;
            }

            var now = DateTime.UtcNow;
            var result = KnownServices.Concat(serviceData.Keys).Distinct()
                .Select(name =>
                {
                    serviceData.TryGetValue(name, out var data);
                    return new
                    {
                        name,
                        isActive = data.lastSeen.HasValue && (now - data.lastSeen.Value).TotalMinutes < 5,
                        lastSeen = data.lastSeen?.ToString("o"),
                        errorCount = data.errorCount,
                        eventCount = data.eventCount
                    };
                })
                .ToList();

            return Results.Ok(result);
        })
        .WithName("GetServicesStatus")
        .WithOpenApi();
    }

    #region Event Helpers

    private static string? GetEventLevel(JsonElement evt)
    {
        if (evt.ValueKind != JsonValueKind.Object) return null;
        return evt.TryGetProperty("Level", out var level) && level.ValueKind == JsonValueKind.String
            ? level.GetString()
            : null;
    }

    private static string? GetEventApplication(JsonElement evt)
    {
        if (evt.ValueKind != JsonValueKind.Object) return null;
        if (!evt.TryGetProperty("Properties", out var props) || props.ValueKind != JsonValueKind.Object)
            return null;
        return props.TryGetProperty("Application", out var app) && app.ValueKind == JsonValueKind.String
            ? app.GetString()
            : null;
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

    private static int ParseIntervalMinutes(string interval)
    {
        if (string.IsNullOrEmpty(interval)) return 60;
        if (interval.EndsWith('h') && int.TryParse(interval[..^1], out var h)) return h * 60;
        if (interval.EndsWith('m') && int.TryParse(interval[..^1], out var m)) return m;
        if (interval.EndsWith('d') && int.TryParse(interval[..^1], out var d)) return d * 24 * 60;
        return 60;
    }

    private static DateTime TruncateToInterval(DateTime dt, int intervalMinutes)
    {
        var totalMinutes = (long)(dt - DateTime.MinValue).TotalMinutes;
        var truncated = totalMinutes / intervalMinutes * intervalMinutes;
        return DateTime.MinValue.AddMinutes(truncated);
    }

    #endregion

    #region Metrics Extraction

    /// <summary>
    /// Extracts service metrics from Seq events API result.
    /// MetricsReporterService logs: "ServiceMetrics {@Metrics}" where @Metrics is
    /// { ServiceName, Metrics: { metric_name: value, ... }, Timestamp }.
    /// Groups by service name and returns the latest metrics for each service.
    /// </summary>
    private static List<object> ExtractServiceMetricsFromEvents(JsonElement response)
    {
        var serviceMetrics = new Dictionary<string, (DateTime Timestamp, Dictionary<string, object> Metrics)>(StringComparer.OrdinalIgnoreCase);
        var now = DateTime.UtcNow;

        if (response.ValueKind != JsonValueKind.Object)
            return [];

        if (!response.TryGetProperty("Events", out var events) || events.ValueKind != JsonValueKind.Array)
            return [];

        foreach (var evt in events.EnumerateArray())
        {
            if (evt.ValueKind != JsonValueKind.Object) continue;

            // Parse timestamp
            var timestamp = GetEventTimestamp(evt);

            // Get service name from Properties.Application
            string? serviceName = null;
            JsonElement props = default;
            var hasProps = evt.TryGetProperty("Properties", out props) && props.ValueKind == JsonValueKind.Object;

            if (hasProps)
            {
                if (props.TryGetProperty("Application", out var appProp) && appProp.ValueKind == JsonValueKind.String)
                    serviceName = appProp.GetString();
            }

            if (string.IsNullOrEmpty(serviceName) || !timestamp.HasValue)
                continue;

            // Skip if we already have more recent metrics for this service
            if (serviceMetrics.ContainsKey(serviceName) && serviceMetrics[serviceName].Timestamp >= timestamp.Value)
                continue;

            // Extract metrics from Properties.Metrics.Metrics (the actual counters dictionary)
            var metrics = new Dictionary<string, object>();

            if (hasProps && props.TryGetProperty("Metrics", out var metricsWrapper))
            {
                // Try nested structure: Metrics.Metrics (the actual Dictionary<string, long>)
                if (metricsWrapper.ValueKind == JsonValueKind.Object &&
                    metricsWrapper.TryGetProperty("Metrics", out var innerMetrics) &&
                    innerMetrics.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in innerMetrics.EnumerateObject())
                    {
                        metrics[prop.Name] = prop.Value.ValueKind switch
                        {
                            JsonValueKind.Number => prop.Value.TryGetInt64(out var l) ? l : prop.Value.GetDouble(),
                            JsonValueKind.String => prop.Value.GetString() ?? "",
                            _ => prop.Value.ToString()
                        };
                    }
                }
                // Fallback: Metrics is directly the dictionary (flat structure)
                else if (metricsWrapper.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in metricsWrapper.EnumerateObject())
                    {
                        // Skip known non-metric properties
                        if (prop.Name is "ServiceName" or "Timestamp") continue;

                        metrics[prop.Name] = prop.Value.ValueKind switch
                        {
                            JsonValueKind.Number => prop.Value.TryGetInt64(out var l) ? l : prop.Value.GetDouble(),
                            JsonValueKind.String => prop.Value.GetString() ?? "",
                            _ => prop.Value.ToString()
                        };
                    }
                }
            }

            serviceMetrics[serviceName] = (timestamp.Value, metrics);
        }

        // Convert to result format
        var result = new List<object>();
        foreach (var (serviceName, (timestamp, metrics)) in serviceMetrics)
        {
            var isActive = (now - timestamp).TotalMinutes < 5;
            result.Add(new
            {
                name = serviceName,
                isActive,
                metrics,
                lastReportTime = timestamp.ToString("o")
            });
        }

        return result;
    }

    #endregion
}
