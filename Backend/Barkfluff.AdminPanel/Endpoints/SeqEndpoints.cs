using Barkfluff.AdminPanel.Data;
using Barkfluff.AdminPanel.Models;
using Barkfluff.AdminPanel.Models.Dtos;
using Barkfluff.AdminPanel.Services;

using System.Text.Json;

namespace Barkfluff.AdminPanel.Endpoints;

public static class SeqEndpoints
{
    public static void MapSeqEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/seq")
            .WithTags("Seq");

        group.MapGet("/events", async (
            SeqService seqService,
            string? application,
            int count = 50,
            string? fromUtc = null,
            string? level = null,
            string? search = null,
            string? correlationId = null,
            string? requestId = null,
            string? userId = null,
            string? afterId = null) =>
        {
            var filter = BuildEventFilter(application, level, search, correlationId, requestId, userId);

            DateTime? fromDate = null;
            if (!string.IsNullOrEmpty(fromUtc) && DateTime.TryParse(fromUtc, out var parsed))
                fromDate = parsed;

            var events = await seqService.GetEventsAsync(filter, count, fromDate, afterId);
            return events.HasValue ? Results.Ok(events.Value) : Results.StatusCode(502);
        })
        .WithName("GetSeqEvents")
        .WithOpenApi();

        group.MapGet("/error-groups", async (
            SeqService seqService,
            string? application = null,
            string? search = null,
            string? correlationId = null,
            string? requestId = null,
            string? userId = null,
            int hours = 24,
            int maxEvents = 5000) =>
        {
            hours = Math.Clamp(hours, 1, 24 * 30);
            maxEvents = Math.Clamp(maxEvents, 100, 20_000);
            var filter = BuildEventFilter(
                application,
                "Error",
                search,
                correlationId,
                requestId,
                userId);
            var events = await seqService.GetAllEventsListAsync(
                filter,
                DateTime.UtcNow.AddHours(-hours),
                maxEvents);
            if (events is null)
                return Results.StatusCode(502);

            return Results.Ok(new
            {
                groups = SeqEventAnalyzer.GroupErrors(events),
                scannedEventCount = events.Count,
                truncated = events.Count >= maxEvents,
                periodHours = hours
            });
        })
        .WithName("GetSeqErrorGroups")
        .WithOpenApi();

        group.MapGet("/deployments", async (
            SeqService seqService,
            string? application = null,
            int hours = 24 * 7) =>
        {
            hours = Math.Clamp(hours, 1, 24 * 90);
            var filterParts = new List<string> { "EventType = 'Deployment'" };
            if (!string.IsNullOrWhiteSpace(application))
                filterParts.Add($"Application = '{EscapeSeq(application)}'");

            var events = await seqService.GetAllEventsListAsync(
                string.Join(" and ", filterParts),
                DateTime.UtcNow.AddHours(-hours),
                1000);
            if (events is null)
                return Results.StatusCode(502);

            var deployments = events.Select(evt => new
            {
                eventId = GetEventId(evt),
                application = SeqEventAnalyzer.ReadProperty(evt, "Application"),
                timestampUtc = GetEventTimestamp(evt),
                kind = SeqEventAnalyzer.ReadProperty(evt, "DeploymentKind"),
                status = SeqEventAnalyzer.ReadProperty(evt, "DeploymentStatus"),
                branch = SeqEventAnalyzer.ReadProperty(evt, "DeploymentBranch"),
                rolledBack = bool.TryParse(SeqEventAnalyzer.ReadProperty(evt, "DeploymentRolledBack"), out var rolledBack) && rolledBack,
                jobId = SeqEventAnalyzer.ReadProperty(evt, "DeployJobId"),
                message = SeqEventAnalyzer.ReadProperty(evt, "DeploymentMessage")
            })
            .Where(deployment => deployment.timestampUtc.HasValue)
            .OrderByDescending(deployment => deployment.timestampUtc)
            .ToList();

            return Results.Ok(deployments);
        })
        .WithName("GetSeqDeployments")
        .WithOpenApi();

        group.MapGet("/deployments/compare", async (
            SeqService seqService,
            string application,
            string atUtc,
            int windowMinutes = 30) =>
        {
            if (string.IsNullOrWhiteSpace(application) ||
                !DateTime.TryParse(atUtc, null, System.Globalization.DateTimeStyles.RoundtripKind, out var deploymentAt))
            {
                return Results.BadRequest(new { message = "Укажите сервис и корректное время деплоя" });
            }

            deploymentAt = deploymentAt.ToUniversalTime();
            windowMinutes = Math.Clamp(windowMinutes, 5, 24 * 60);
            var filter = $"Application = '{EscapeSeq(application)}' and @Level in ['Error', 'Fatal']";
            var window = TimeSpan.FromMinutes(windowMinutes);
            var beforeTask = seqService.CountFilteredEventsAsync(
                filter,
                deploymentAt.Subtract(window),
                deploymentAt.AddTicks(-1));
            var afterTask = seqService.CountFilteredEventsAsync(
                filter,
                deploymentAt,
                deploymentAt.Add(window));
            await Task.WhenAll(beforeTask, afterTask);

            if (!beforeTask.Result.HasValue || !afterTask.Result.HasValue)
                return Results.StatusCode(502);

            var before = beforeTask.Result.Value;
            var after = afterTask.Result.Value;
            return Results.Ok(new
            {
                application,
                deploymentAtUtc = deploymentAt,
                windowMinutes,
                beforeErrorCount = before,
                afterErrorCount = after,
                delta = after - before
            });
        })
        .WithName("CompareSeqDeployment")
        .WithOpenApi();

        group.MapGet("/services", () =>
        {
            // Только микросервисы BarkFluff — инфраструктура (Seq/Minio/RabbitMQ/Redis/PostgreSQL)
            // логи в Seq не отдаёт, поэтому в фильтре логов её не показываем.
            var logServices = PlatformServiceRegistry.BarkFluff
                .Select(s => s.Name)
                .ToArray();
            return Results.Ok(logServices);
        })
        .WithName("GetSeqServices")
        .WithOpenApi();

        // ---- Cache-based dashboard endpoints ----

        group.MapGet("/dashboard/kpis", async (
            MetricsCacheDbContext cache,
            SeqService seqService,
            int hours = 24) =>
        {
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
            int hours = 24,
            string interval = "1h") =>
        {
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

        group.MapGet("/dashboard/metric-groups", () =>
        {
            return Results.Ok(new
            {
                groups = MetricsCatalog.Services.Select(service => new
                {
                    serviceName = service.Name,
                    title = service.Title,
                    expandedByDefault = service.ExpandedByDefault,
                    metrics = service.Metrics.Select(metric => new
                    {
                        id = metric.Id,
                        title = metric.Title,
                        unit = metric.Unit,
                        kind = metric.Kind
                    })
                })
            });
        })
        .WithName("GetDashboardMetricGroups")
        .WithOpenApi();

        group.MapGet("/dashboard/metric-groups/{serviceName}", (
            MetricsCacheDbContext cache,
            string serviceName,
            int hours = 72) =>
        {
            var service = MetricsCatalog.Find(serviceName);
            if (service is null) return Results.NotFound();

            hours = Math.Clamp(hours, 1, 72);
            var cutoff = TruncateToHour(DateTime.UtcNow).AddHours(-hours);
            var rows = cache.HourlyServiceMetrics
                .Find(x => x.ServiceName == service.Name && x.HourUtc >= cutoff && x.SchemaVersion == 2)
                .OrderBy(x => x.HourUtc)
                .ToList();
            var rowsByHour = rows.ToDictionary(x => x.HourUtc);
            var hoursInRange = Enumerable.Range(0, hours + 1)
                .Select(offset => cutoff.AddHours(offset));

            return Results.Ok(new
            {
                serviceName = service.Name,
                title = service.Title,
                periodHours = hours,
                metrics = service.Metrics.Select(metric => new
                {
                    id = metric.Id,
                    title = metric.Title,
                    unit = metric.Unit,
                    kind = metric.Kind,
                    points = hoursInRange.Select(hour =>
                    {
                        if (!rowsByHour.TryGetValue(hour, out var row))
                            return new { hour = hour.ToString("o"), value = (long?)null };
                        var values = metric.Kind == "gauge" ? row.Gauges : row.Counters;
                        return values.TryGetValue(metric.Id, out var value)
                            ? new { hour = row.HourUtc.ToString("o"), value = (long?)value }
                            : new { hour = row.HourUtc.ToString("o"), value = (long?)null };
                    })
                })
            });
        })
        .WithName("GetDashboardMetricGroup")
        .WithOpenApi();

        group.MapGet("/services/status", async (
            SeqService seqService,
            DockerService dockerService,
            DockerRegistryService dockerRegistryService,
            int hours = 24) =>
        {
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
            Dictionary<string, ContainerStatusDto>? containersByName = null;
            try
            {
                var containers = await dockerService.GetContainersAsync();
                containersByName = containers
                    .Where(c => !string.IsNullOrEmpty(c.Name) && !string.IsNullOrEmpty(c.State))
                    .ToDictionary(
                        c => c.Name.TrimStart('/'),
                        c => c,
                        StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                // Если Docker недоступен — fallback на Seq-логику
            }

            var now = DateTime.UtcNow;

            async Task<object> BuildEntryAsync(string name, ContainerStatusDto? container)
            {
                serviceData.TryGetValue(name, out var data);
                var dockerState = container?.State;
                bool isActive = dockerState != null
                    ? dockerState == "running"
                    : data.lastSeen.HasValue && (now - data.lastSeen.Value).TotalMinutes < 5;

                var versionStatus = container is null
                    ? new ImageVersionStatusDto()
                    : await dockerRegistryService.GetVersionStatusAsync(container.Image, container.ImageDigest);

                return new
                {
                    name,
                    isActive,
                    dockerState,
                    lastSeen = data.lastSeen?.ToString("o"),
                    errorCount = data.errorCount,
                    eventCount = data.eventCount,
                    currentVersion = versionStatus.CurrentVersion,
                    latestVersion = versionStatus.LatestVersion,
                    updateAvailable = versionStatus.UpdateAvailable
                };
            }

            List<object> result;
            if (containersByName != null)
            {
                // Динамически: показываем только реально присутствующие контейнеры известных сервисов.
                // Так на проде не светятся сервисы, которых нет в развёрнутом compose (например Minio,
                // если используется арендованный S3).
                var containerToService = PlatformServiceRegistry.ServiceToContainer
                    .ToDictionary(kv => kv.Value, kv => kv.Key, StringComparer.OrdinalIgnoreCase);

                result = (await Task.WhenAll(containersByName
                    .Where(cs => containerToService.ContainsKey(cs.Key))
                    .OrderBy(cs => containerToService[cs.Key], StringComparer.Ordinal)
                    .Select(cs => BuildEntryAsync(containerToService[cs.Key], cs.Value))))
                    .Cast<object>()
                    .ToList();
            }
            else
            {
                // Docker недоступен — fallback на данные Seq.
                result = (await Task.WhenAll(PlatformServiceRegistry.All
                    .Select(s => s.Name)
                    .Concat(serviceData.Keys).Distinct()
                    .Select(name => BuildEntryAsync(name, null))))
                    .Cast<object>()
                    .ToList();
            }

            return Results.Ok(result);
        })
        .WithName("GetServicesStatus")
        .WithOpenApi();
    }

    #region Helpers

    private static string? BuildEventFilter(
        string? application,
        string? level,
        string? search,
        string? correlationId,
        string? requestId,
        string? userId)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(application))
            parts.Add($"Application = '{EscapeSeq(application)}'");

        if (!string.IsNullOrWhiteSpace(level))
        {
            parts.Add(level.Equals("Error", StringComparison.OrdinalIgnoreCase)
                ? "@Level in ['Error', 'Fatal']"
                : $"@Level = '{EscapeSeq(level)}'");
        }

        if (!string.IsNullOrWhiteSpace(search))
            parts.Add($"IndexOf(@Message, '{EscapeSeq(search)}') >= 0");

        if (!string.IsNullOrWhiteSpace(correlationId))
        {
            var value = EscapeSeq(correlationId);
            parts.Add($"(ToString(CorrelationId) = '{value}' or ToString(TraceId) = '{value}')");
        }

        if (!string.IsNullOrWhiteSpace(requestId))
        {
            var value = EscapeSeq(requestId);
            parts.Add($"(ToString(RequestId) = '{value}' or ToString(TraceIdentifier) = '{value}')");
        }

        if (!string.IsNullOrWhiteSpace(userId))
        {
            var value = EscapeSeq(userId);
            parts.Add($"(ToString(UserId) = '{value}' or ToString(AffectedUserId) = '{value}' or ToString(TargetUserId) = '{value}' or ToString(SubjectUserId) = '{value}')");
        }

        return parts.Count == 0 ? null : string.Join(" and ", parts);
    }

    private static string EscapeSeq(string value) => value.Replace("'", "''");

    private static string? GetEventId(JsonElement evt)
    {
        if (evt.ValueKind != JsonValueKind.Object) return null;
        if (evt.TryGetProperty("Id", out var id) && id.ValueKind == JsonValueKind.String)
            return id.GetString();
        if (evt.TryGetProperty("id", out var lowerId) && lowerId.ValueKind == JsonValueKind.String)
            return lowerId.GetString();
        return null;
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
