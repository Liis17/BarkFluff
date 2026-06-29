using Barkfluff.AdminPanel.Data;
using Barkfluff.AdminPanel.Models;
using Barkfluff.AdminPanel.Services;

using System.Text.Json;

namespace Barkfluff.AdminPanel.Endpoints;

public static class SeqEndpoints
{
    private static readonly string[] KnownServices =
    [
        // Микросервисы BarkFluff
        "BarkFluff.Identity",
        "BarkFluff.Users",
        "BarkFluff.Messages",
        "BarkFluff.Files",
        "BarkFluff.Updates",
        "BarkFluff.Notification",
        "BarkFluff.Beacon",
        "BarkFluff.FastAuth",
        "BarkFluff.Onliner",
        "BarkFluff.CloudMessaging",
        "BarkFluff.Web",
        "BarkFluff.Configuration",
        "BarkFluff.Developers",
        "BarkFluff.Calls",
        // Инфраструктурные сервисы
        "Seq",
        "Minio",
        "RabbitMQ",
        "Redis",
        "PostgreSQL"
    ];

    private static readonly Dictionary<string, string> ServiceToContainerMap =
        new(StringComparer.OrdinalIgnoreCase)
        {
            { "BarkFluff.Beacon",        "beacon" },
            { "BarkFluff.Configuration", "configuration" },
            { "BarkFluff.Files",         "files" },
            { "BarkFluff.Identity",      "identity" },
            { "BarkFluff.Messages",      "messages" },
            { "BarkFluff.Notification",  "notification" },
            { "BarkFluff.Users",         "users" },
            { "BarkFluff.FastAuth",      "fast-auth" },
            { "BarkFluff.Updates",       "updates" },
            { "BarkFluff.Onliner",       "onliner" },
            { "BarkFluff.CloudMessaging","cloud-messaging" },
            { "BarkFluff.Web",           "web" },
            { "BarkFluff.Developers",    "developers" },
            { "BarkFluff.Calls",         "calls" },
            { "Seq",                     "seq" },
            { "Minio",                   "minio" },
            { "RabbitMQ",                "rabbitmq" },
            { "Redis",                   "redis" },
            { "PostgreSQL",              "postgres_barkfluff" },
        };

    public static void MapSeqEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/seq")
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
                var sanitized = search.Replace("'", "''");
                filterParts.Add($"IndexOf(@Message, '{sanitized}') >= 0");
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

            // Только микросервисы BarkFluff — инфраструктура (Seq/Minio/RabbitMQ/Redis/PostgreSQL)
            // логи в Seq не отдаёт, поэтому в фильтре логов её не показываем.
            var logServices = KnownServices
                .Where(s => s.StartsWith("BarkFluff.", StringComparison.Ordinal))
                .ToArray();
            return Results.Ok(logServices);
        })
        .WithName("GetSeqServices")
        .WithOpenApi();

        // ---- Cache-based dashboard endpoints ----

        group.MapGet("/dashboard/kpis", async (
            MetricsCacheDbContext cache,
            SeqService seqService,
            HttpContext context,
            int hours = 24) =>
        {
            if (context.Items["AuthToken"] is not AuthToken)
                return Results.Unauthorized();

            var cutoff = TruncateToHour(DateTime.UtcNow).AddHours(-hours);
            var stats = cache.HourlyStats.Find(x => x.HourUtc >= cutoff).ToList();

            // Fallback: if cache is empty, query Seq directly
            if (stats.Count == 0)
            {
                var fromDateUtc = DateTime.UtcNow.AddHours(-hours);
                var events = await seqService.GetAllEventsListAsync(null, fromDateUtc, 50000);
                if (events == null)
                    return Results.StatusCode(502);

                long total = events.Count;
                long errors = events.Count(e => GetEventLevel(e) is "Error" or "Fatal");
                long warnings = events.Count(e => GetEventLevel(e) == "Warning");
                var perSvc = events
                    .Select(GetEventApplication)
                    .Where(a => !string.IsNullOrEmpty(a))
                    .GroupBy(a => a!)
                    .ToDictionary(g => g.Key, g => (long)g.Count());

                return Results.Ok(new
                {
                    totalEvents = total,
                    errorCount = errors,
                    warningCount = warnings,
                    perService = perSvc,
                    periodHours = hours
                });
            }

            long totalEvents = stats.Sum(x => x.TotalEvents);
            long errorCount = stats.Sum(x => x.ErrorCount);
            long warningCount = stats.Sum(x => x.WarningCount);

            var perService = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in stats)
            {
                foreach (var (svc, count) in s.PerService)
                {
                    perService[svc] = perService.GetValueOrDefault(svc) + count;
                }
            }

            return Results.Ok(new
            {
                totalEvents,
                errorCount,
                warningCount,
                perService,
                periodHours = hours
            });
        })
        .WithName("GetDashboardKpis")
        .WithOpenApi();

        group.MapGet("/dashboard/traffic", async (
            MetricsCacheDbContext cache,
            SeqService seqService,
            HttpContext context,
            int hours = 24,
            string interval = "1h") =>
        {
            if (context.Items["AuthToken"] is not AuthToken)
                return Results.Unauthorized();

            var now = DateTime.UtcNow;
            var currentHour = TruncateToHour(now);
            var cutoff = currentHour.AddHours(-hours);

            var trafficData = cache.HourlyTraffic.Find(x => x.HourUtc >= cutoff)
                .OrderBy(x => x.HourUtc)
                .ToDictionary(x => x.HourUtc);

            // Fallback: if cache is empty, query Seq directly
            if (trafficData.Count == 0)
            {
                var fromDateUtc = DateTime.UtcNow.AddHours(-hours);
                var events = await seqService.GetAllEventsListAsync(null, fromDateUtc, 50000);
                if (events == null)
                    return Results.StatusCode(502);

                var allBuckets = new Dictionary<DateTime, long>();
                var errorBuckets = new Dictionary<DateTime, long>();
                var warningBuckets = new Dictionary<DateTime, long>();

                foreach (var evt in events)
                {
                    var ts = GetEventTimestamp(evt);
                    if (!ts.HasValue) continue;

                    var bucket = TruncateToHour(ts.Value);
                    allBuckets[bucket] = allBuckets.GetValueOrDefault(bucket) + 1;

                    var level = GetEventLevel(evt);
                    if (level is "Error" or "Fatal")
                        errorBuckets[bucket] = errorBuckets.GetValueOrDefault(bucket) + 1;
                    if (level == "Warning")
                        warningBuckets[bucket] = warningBuckets.GetValueOrDefault(bucket) + 1;
                }

                var fbAll = new List<object>();
                var fbErrors = new List<object>();
                var fbWarnings = new List<object>();
                var fbBucket = cutoff;
                while (fbBucket <= currentHour)
                {
                    fbAll.Add(new { timestamp = fbBucket.ToString("o"), count = allBuckets.GetValueOrDefault(fbBucket) });
                    fbErrors.Add(new { timestamp = fbBucket.ToString("o"), count = errorBuckets.GetValueOrDefault(fbBucket) });
                    fbWarnings.Add(new { timestamp = fbBucket.ToString("o"), count = warningBuckets.GetValueOrDefault(fbBucket) });
                    fbBucket = fbBucket.AddHours(1);
                }

                return Results.Ok(new { all = fbAll, errors = fbErrors, warnings = fbWarnings });
            }

            // Generate continuous time series from cache
            var allData = new List<object>();
            var errorsData = new List<object>();
            var warningsData = new List<object>();

            var bucketTime = cutoff;
            while (bucketTime <= currentHour)
            {
                trafficData.TryGetValue(bucketTime, out var bucket);
                allData.Add(new { timestamp = bucketTime.ToString("o"), count = bucket?.AllCount ?? 0 });
                errorsData.Add(new { timestamp = bucketTime.ToString("o"), count = bucket?.ErrorCount ?? 0 });
                warningsData.Add(new { timestamp = bucketTime.ToString("o"), count = bucket?.WarningCount ?? 0 });
                bucketTime = bucketTime.AddHours(1);
            }

            return Results.Ok(new
            {
                all = allData,
                errors = errorsData,
                warnings = warningsData
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

        group.MapGet("/dashboard/service-metrics", async (
            MetricsCacheDbContext cache,
            SeqService seqService,
            HttpContext context,
            int hours = 12) =>
        {
            if (context.Items["AuthToken"] is not AuthToken)
                return Results.Unauthorized();

            var currentHour = TruncateToHour(DateTime.UtcNow);
            var cutoff = currentHour.AddHours(-hours);

            var entries = cache.HourlyServiceMetrics
                .Find(x => x.HourUtc >= cutoff)
                .OrderBy(x => x.HourUtc)
                .ToList();

            // Fallback: if cache is empty, query Seq directly for ServiceMetrics events
            if (entries.Count == 0)
            {
                var fromDateUtc = DateTime.UtcNow.AddHours(-hours);
                var filter = "@Message like 'ServiceMetrics%'";
                var events = await seqService.GetAllEventsListAsync(filter, fromDateUtc, 5000);

                if (events == null || events.Count == 0)
                    return Results.Ok(new { periodHours = hours, services = new List<object>() });

                // Group by service + hour
                var grouped = new Dictionary<(string svc, DateTime hour), Dictionary<string, long>>();

                foreach (var evt in events)
                {
                    var svcName = GetEventApplication(evt);
                    var ts = GetEventTimestamp(evt);
                    if (string.IsNullOrEmpty(svcName) || !ts.HasValue) continue;

                    var hour = TruncateToHour(ts.Value);
                    var key = (svcName, hour);

                    var metrics = ExtractMetricValuesLong(evt);
                    if (metrics.Count > 0)
                        grouped[key] = metrics; // last write wins (latest per hour)
                }

                var fbGroups = grouped
                    .GroupBy(kv => kv.Key.svc, StringComparer.OrdinalIgnoreCase)
                    .Select(g => new
                    {
                        name = g.Key,
                        timeSeries = g.OrderBy(kv => kv.Key.hour).Select(kv => new
                        {
                            hour = kv.Key.hour.ToString("o"),
                            metrics = kv.Value
                        }).ToList()
                    })
                    .OrderBy(x => x.name)
                    .ToList();

                return Results.Ok(new { periodHours = hours, services = fbGroups });
            }

            // Group by service name from cache
            var serviceGroups = entries
                .GroupBy(x => x.ServiceName, StringComparer.OrdinalIgnoreCase)
                .Select(g => new
                {
                    name = g.Key,
                    timeSeries = g.Select(e => new
                    {
                        hour = e.HourUtc.ToString("o"),
                        metrics = e.Metrics
                    }).ToList()
                })
                .OrderBy(x => x.name)
                .ToList();

            return Results.Ok(new
            {
                periodHours = hours,
                services = serviceGroups
            });
        })
        .WithName("GetDashboardServiceMetrics")
        .WithOpenApi();

        group.MapGet("/dashboard/service-metrics/{serviceName}", async (
            MetricsCacheDbContext cache,
            SeqService seqService,
            HttpContext context,
            string serviceName,
            int hours = 12) =>
        {
            if (context.Items["AuthToken"] is not AuthToken)
                return Results.Unauthorized();

            var now = DateTime.UtcNow;
            var currentHour = TruncateToHour(now);
            var cutoff = currentHour.AddHours(-hours);

            // Determine which hours we need
            var neededHours = new List<DateTime>();
            var cachedData = new Dictionary<DateTime, Dictionary<string, long>>();

            for (var h = cutoff; h <= currentHour; h = h.AddHours(1))
            {
                if (h == currentHour)
                {
                    // Current (incomplete) hour — always re-fetch
                    neededHours.Add(h);
                    continue;
                }

                var cached = cache.HourlyServiceMetrics
                    .FindOne(x => x.HourUtc == h && x.ServiceName == serviceName);

                if (cached != null)
                {
                    cachedData[h] = cached.Metrics;
                }
                else
                {
                    neededHours.Add(h);
                }
            }

            // Fetch missing hours from Seq
            if (neededHours.Count > 0)
            {
                var fetchFrom = neededHours.Min();
                var fetchTo = neededHours.Max().AddHours(1);

                var filter = $"Application = '{serviceName.Replace("'", "''")}' and @Message like 'ServiceMetrics%'";
                var events = await seqService.GetAllEventsListAsync(filter, fetchFrom, 5000);

                if (events != null)
                {
                    // Group fetched events by hour
                    var grouped = new Dictionary<DateTime, Dictionary<string, long>>();
                    foreach (var evt in events)
                    {
                        var ts = GetEventTimestamp(evt);
                        if (!ts.HasValue) continue;

                        var hour = TruncateToHour(ts.Value);
                        if (!neededHours.Contains(hour)) continue;

                        var metrics = ExtractMetricValuesLong(evt);
                        if (metrics.Count > 0)
                            grouped[hour] = metrics; // last write wins
                    }

                    // Save completed hours to cache, merge into result
                    foreach (var h in neededHours)
                    {
                        if (grouped.TryGetValue(h, out var metrics))
                        {
                            cachedData[h] = metrics;

                            // Only cache completed hours (not the current one)
                            if (h != currentHour)
                            {
                                // Remove old entry if exists, then insert
                                cache.HourlyServiceMetrics.DeleteMany(x =>
                                    x.HourUtc == h && x.ServiceName == serviceName);
                                cache.HourlyServiceMetrics.Insert(new HourlyServiceMetrics
                                {
                                    HourUtc = h,
                                    ServiceName = serviceName,
                                    Metrics = metrics
                                });
                            }
                        }
                        // If no data for this hour, leave it empty (no cache entry)
                    }
                }
            }

            // Build time series response
            var timeSeries = new List<object>();
            for (var h = cutoff; h <= currentHour; h = h.AddHours(1))
            {
                if (cachedData.TryGetValue(h, out var m))
                {
                    timeSeries.Add(new { hour = h.ToString("o"), metrics = m });
                }
            }

            return Results.Ok(new { serviceName, periodHours = hours, timeSeries });
        })
        .WithName("GetDashboardServiceMetricsPerService")
        .WithOpenApi();

        group.MapGet("/services/status", async (
            SeqService seqService,
            DockerService dockerService,
            HttpContext context,
            int hours = 24) =>
        {
            if (context.Items["AuthToken"] is not AuthToken)
                return Results.Unauthorized();

            var fromDateUtc = DateTime.UtcNow.AddHours(-hours);

            var events = await seqService.GetAllEventsListAsync(null, fromDateUtc, 5000);
            if (events == null)
                return Results.StatusCode(502);

            // Aggregate per-service stats from Seq
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

            // Получаем статусы контейнеров Docker
            Dictionary<string, string>? containerStates = null;
            try
            {
                var containers = await dockerService.GetContainersAsync();
                containerStates = containers
                    .Where(c => !string.IsNullOrEmpty(c.Name) && !string.IsNullOrEmpty(c.State))
                    .ToDictionary(
                        c => c.Name.TrimStart('/'),
                        c => c.State!,
                        StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                // Если Docker недоступен — fallback на Seq-логику
            }

            var now = DateTime.UtcNow;

            object BuildEntry(string name, string? dockerState)
            {
                serviceData.TryGetValue(name, out var data);
                bool isActive = dockerState != null
                    ? dockerState == "running"
                    : data.lastSeen.HasValue && (now - data.lastSeen.Value).TotalMinutes < 5;
                return new
                {
                    name,
                    isActive,
                    dockerState,
                    lastSeen = data.lastSeen?.ToString("o"),
                    errorCount = data.errorCount,
                    eventCount = data.eventCount
                };
            }

            List<object> result;
            if (containerStates != null)
            {
                // Динамически: показываем только реально присутствующие контейнеры известных сервисов.
                // Так на проде не светятся сервисы, которых нет в развёрнутом compose (например Minio,
                // если используется арендованный S3).
                var containerToService = ServiceToContainerMap
                    .ToDictionary(kv => kv.Value, kv => kv.Key, StringComparer.OrdinalIgnoreCase);

                result = containerStates
                    .Where(cs => containerToService.ContainsKey(cs.Key))
                    .OrderBy(cs => containerToService[cs.Key], StringComparer.Ordinal)
                    .Select(cs => BuildEntry(containerToService[cs.Key], cs.Value))
                    .ToList();
            }
            else
            {
                // Docker недоступен — fallback на данные Seq.
                result = KnownServices.Concat(serviceData.Keys).Distinct()
                    .Select(name => BuildEntry(name, null))
                    .ToList();
            }

            return Results.Ok(result);
        })
        .WithName("GetServicesStatus")
        .WithOpenApi();
    }

    #region Helpers

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

    private static DateTime? GetEventTimestamp(JsonElement evt)
    {
        if (evt.ValueKind != JsonValueKind.Object) return null;
        if (!evt.TryGetProperty("Timestamp", out var ts) || ts.ValueKind != JsonValueKind.String)
            return null;
        return DateTime.TryParse(ts.GetString(), null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt)
            ? dt
            : null;
    }

    #endregion

    #region Metrics Extraction

    private static List<object> ExtractServiceMetricsFromEvents(JsonElement response)
    {
        var serviceMetrics = new Dictionary<string, (DateTime Timestamp, Dictionary<string, object> Metrics)>(StringComparer.OrdinalIgnoreCase);
        var now = DateTime.UtcNow;

        var eventsList = SeqService.ExtractEventsArray(response);
        if (eventsList == null || eventsList.Count == 0)
            return [];

        foreach (var evt in eventsList)
        {
            if (evt.ValueKind != JsonValueKind.Object) continue;

            var timestamp = GetEventTimestamp(evt);

            string? serviceName = GetEventApplication(evt);

            if (string.IsNullOrEmpty(serviceName) || !timestamp.HasValue)
                continue;

            if (serviceMetrics.ContainsKey(serviceName) && serviceMetrics[serviceName].Timestamp >= timestamp.Value)
                continue;

            var metrics = new Dictionary<string, object>();

            if (evt.TryGetProperty("Properties", out var props))
            {
                var metricsVal = GetPropertyFromSeqProps(props, "Metrics");
                if (metricsVal != null)
                {
                    var metricsWrapper = metricsVal.Value;
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
                    else if (metricsWrapper.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var prop in metricsWrapper.EnumerateObject())
                        {
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
            }

            serviceMetrics[serviceName] = (timestamp.Value, metrics);
        }

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

    #region Metric Value Extraction (for fallback)

    private static Dictionary<string, long> ExtractMetricValuesLong(JsonElement evt)
    {
        var metrics = new Dictionary<string, long>();
        if (evt.ValueKind != JsonValueKind.Object) return metrics;

        if (!evt.TryGetProperty("Properties", out var props)) return metrics;

        var metricsVal = GetPropertyFromSeqProps(props, "Metrics");
        if (metricsVal == null || metricsVal.Value.ValueKind != JsonValueKind.Object) return metrics;

        var metricsWrapper = metricsVal.Value;

        // Try nested structure: Metrics.Metrics
        JsonElement metricsObj;
        if (metricsWrapper.TryGetProperty("Metrics", out var innerMetrics) && innerMetrics.ValueKind == JsonValueKind.Object)
            metricsObj = innerMetrics;
        else
            metricsObj = metricsWrapper;

        foreach (var prop in metricsObj.EnumerateObject())
        {
            if (prop.Name is "ServiceName" or "Timestamp") continue;
            if (prop.Value.ValueKind == JsonValueKind.Number && prop.Value.TryGetInt64(out var val))
                metrics[prop.Name] = val;
        }

        return metrics;
    }

    #endregion
}
