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

            var totalTask = seqService.RunSqlQueryAsync(
                "select count(*) as Count from stream",
                fromDateUtc);

            var errorsTask = seqService.RunSqlQueryAsync(
                "select count(*) as Count from stream where @Level in ['Error', 'Fatal']",
                fromDateUtc);

            var warningsTask = seqService.RunSqlQueryAsync(
                "select count(*) as Count from stream where @Level = 'Warning'",
                fromDateUtc);

            var perServiceTask = seqService.RunSqlQueryAsync(
                "select count(*) as Count, Application from stream group by Application",
                fromDateUtc);

            await Task.WhenAll(totalTask, errorsTask, warningsTask, perServiceTask);

            var totalResult = await totalTask;
            var errorsResult = await errorsTask;
            var warningsResult = await warningsTask;
            var perServiceResult = await perServiceTask;

            if (totalResult == null || errorsResult == null || warningsResult == null || perServiceResult == null)
                return Results.StatusCode(502);

            var totalCount = ExtractScalarCount(totalResult.Value);
            var errorCount = ExtractScalarCount(errorsResult.Value);
            var warningCount = ExtractScalarCount(warningsResult.Value);
            var perService = ExtractGroupedCounts(perServiceResult.Value);

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

            var allTask = seqService.RunSqlQueryAsync(
                $"select count(*) as Count from stream group by time({interval})",
                fromDateUtc);

            var errorsTask = seqService.RunSqlQueryAsync(
                $"select count(*) as Count from stream where @Level in ['Error', 'Fatal'] group by time({interval})",
                fromDateUtc);

            await Task.WhenAll(allTask, errorsTask);

            var allResult = await allTask;
            var errorsResult = await errorsTask;

            if (allResult == null || errorsResult == null)
                return Results.StatusCode(502);

            var allData = ExtractTimeSeriesData(allResult.Value);
            var errorsData = ExtractTimeSeriesData(errorsResult.Value);

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

            var fromDateUtc = DateTime.UtcNow.AddHours(-hours);

            // Сначала получаем события через events API, затем парсим
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

            var errorsTask = seqService.RunSqlQueryAsync(
                "select count(*) as Count, Application from stream where @Level in ['Error', 'Fatal'] group by Application",
                fromDateUtc);

            var eventsTask = seqService.RunSqlQueryAsync(
                "select count(*) as Count, Application from stream group by Application",
                fromDateUtc);

            var lastSeenTask = seqService.RunSqlQueryAsync(
                "select max(@Timestamp) as LastSeen, Application from stream group by Application",
                fromDateUtc);

            await Task.WhenAll(errorsTask, eventsTask, lastSeenTask);

            var errorsResult = await errorsTask;
            var eventsResult = await eventsTask;
            var lastSeenResult = await lastSeenTask;

            if (errorsResult == null || eventsResult == null || lastSeenResult == null)
                return Results.StatusCode(502);

            var errorCounts = ExtractGroupedCounts(errorsResult.Value);
            var eventCounts = ExtractGroupedCounts(eventsResult.Value);
            var lastSeens = ExtractGroupedTimestamps(lastSeenResult.Value);

            var now = DateTime.UtcNow;
            var result = new List<object>();

            // Include all known services even if they have no data
            var allServiceNames = KnownServices.Concat(eventCounts.Keys).Distinct();

            foreach (var serviceName in allServiceNames)
            {
                lastSeens.TryGetValue(serviceName, out var lastSeenValue);
                var isActive = lastSeens.ContainsKey(serviceName) && (now - lastSeens[serviceName]).TotalMinutes < 5;
                errorCounts.TryGetValue(serviceName, out var errorCount);
                eventCounts.TryGetValue(serviceName, out var eventCount);

                result.Add(new
                {
                    name = serviceName,
                    isActive,
                    lastSeen = lastSeens.ContainsKey(serviceName) ? lastSeens[serviceName].ToString("o") : null,
                    errorCount,
                    eventCount
                });
            }

            return Results.Ok(result);
        })
        .WithName("GetServicesStatus")
        .WithOpenApi();
    }

    #region SQL Response Parsers

    /// <summary>
    /// Extracts a single scalar count value from Seq SQL query result.
    /// Expected format: { Columns: [{Name: "Count"}], Rows: [[123]] }
    /// </summary>
    private static long ExtractScalarCount(JsonElement? response)
    {
        if (response == null) return 0;
        if (!response.Value.TryGetProperty("Rows", out var rows)) return 0;
        if (rows.GetArrayLength() == 0) return 0;
        if (rows[0].GetArrayLength() == 0) return 0;

        var value = rows[0][0];
        if (value.ValueKind == JsonValueKind.Number)
            return value.GetInt64();
        if (value.ValueKind == JsonValueKind.String && long.TryParse(value.GetString(), out var num))
            return num;

        return 0;
    }

    /// <summary>
    /// Extracts grouped counts from Seq SQL query result.
    /// Expected format: { Columns: [{Name: "Count"}, {Name: "Application"}], Rows: [[123, "ServiceName"], ...] }
    /// Assumes the second column is the group key and first is the count.
    /// </summary>
    private static Dictionary<string, long> ExtractGroupedCounts(JsonElement? response)
    {
        var result = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

        if (response == null) return result;
        if (!response.Value.TryGetProperty("Rows", out var rows)) return result;

        foreach (var row in rows.EnumerateArray())
        {
            if (row.GetArrayLength() < 2) continue;

            var count = row[0].ValueKind == JsonValueKind.Number
                ? row[0].GetInt64()
                : 0L;

            var key = row[1].ValueKind == JsonValueKind.String
                ? row[1].GetString()
                : null;

            if (!string.IsNullOrEmpty(key))
            {
                result[key] = count;
            }
        }

        return result;
    }

    /// <summary>
    /// Extracts time series data from Seq SQL query result with time() grouping.
    /// Expected format: { Columns: [{Name: "Count"}, {Name: "..."}], Rows: [[123, "2025-02-13T10:00:00Z"], ...] }
    /// </summary>
    private static List<(DateTime Timestamp, long Count)> ExtractTimeSeriesData(JsonElement? response)
    {
        var result = new List<(DateTime Timestamp, long Count)>();

        if (response == null) return result;
        if (!response.Value.TryGetProperty("Rows", out var rows)) return result;

        foreach (var row in rows.EnumerateArray())
        {
            if (row.GetArrayLength() < 2) continue;

            var count = row[0].ValueKind == JsonValueKind.Number
                ? row[0].GetInt64()
                : 0L;

            // Try to parse timestamp from second column
            DateTime? timestamp = null;
            if (row[1].ValueKind == JsonValueKind.String)
            {
                var tsStr = row[1].GetString();
                if (DateTime.TryParse(tsStr, out var ts))
                {
                    timestamp = ts;
                }
            }

            if (timestamp.HasValue)
            {
                result.Add((timestamp.Value, count));
            }
        }

        return result.OrderBy(x => x.Timestamp).ToList();
    }

    /// <summary>
    /// Extracts service metrics from Seq SQL query result.
    /// Expected format: { Columns: [...], Rows: [["2025-02-13T10:00:00Z", "ServiceName", "{...metrics...}"], ...] }
    /// Groups by service name and returns the latest metrics for each service.
    /// </summary>
    private static List<object> ExtractServiceMetrics(JsonElement response)
    {
        var serviceMetrics = new Dictionary<string, (DateTime Timestamp, Dictionary<string, object> Metrics)>(StringComparer.OrdinalIgnoreCase);
        var now = DateTime.UtcNow;

        if (!response.TryGetProperty("Rows", out var rows))
            return [];

        foreach (var row in rows.EnumerateArray())
        {
            if (row.GetArrayLength() < 3) continue;

            // Parse timestamp
            DateTime? timestamp = null;
            if (row[0].ValueKind == JsonValueKind.String)
            {
                var tsStr = row[0].GetString();
                if (DateTime.TryParse(tsStr, out var ts))
                    timestamp = ts;
            }

            // Get service name
            var serviceName = row[1].ValueKind == JsonValueKind.String
                ? row[1].GetString()
                : null;

            if (string.IsNullOrEmpty(serviceName) || !timestamp.HasValue)
                continue;

            // Skip if we already have more recent metrics for this service
            if (serviceMetrics.ContainsKey(serviceName) && serviceMetrics[serviceName].Timestamp >= timestamp.Value)
                continue;

            // Parse metrics JSON
            var metrics = new Dictionary<string, object>();
            if (row[2].ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in row[2].EnumerateObject())
                {
                    metrics[prop.Name] = prop.Value.ValueKind switch
                    {
                        JsonValueKind.Number => prop.Value.TryGetInt64(out var l) ? l : prop.Value.GetDouble(),
                        JsonValueKind.String => prop.Value.GetString() ?? "",
                        JsonValueKind.True => true,
                        JsonValueKind.False => false,
                        _ => prop.Value.ToString()
                    };
                }
            }
            else if (row[2].ValueKind == JsonValueKind.String)
            {
                try
                {
                    var metricsJson = row[2].GetString();
                    if (!string.IsNullOrEmpty(metricsJson))
                    {
                        using var doc = JsonDocument.Parse(metricsJson);
                        foreach (var prop in doc.RootElement.EnumerateObject())
                        {
                            metrics[prop.Name] = prop.Value.ValueKind switch
                            {
                                JsonValueKind.Number => prop.Value.TryGetInt64(out var l) ? l : prop.Value.GetDouble(),
                                JsonValueKind.String => prop.Value.GetString() ?? "",
                                JsonValueKind.True => true,
                                JsonValueKind.False => false,
                                _ => prop.Value.ToString()
                            };
                        }
                    }
                }
                catch
                {
                    // Ignore parse errors
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

    /// <summary>
    /// Extracts grouped timestamp values from Seq SQL query result.
    /// Expected format: { Columns: [{Name: "LastSeen"}, {Name: "Application"}], Rows: [["2025-02-13T10:00:00Z", "ServiceName"], ...] }
    /// </summary>
    private static Dictionary<string, DateTime> ExtractGroupedTimestamps(JsonElement? response)
    {
        var result = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

        if (response == null) return result;
        if (!response.Value.TryGetProperty("Rows", out var rows)) return result;

        foreach (var row in rows.EnumerateArray())
        {
            if (row.GetArrayLength() < 2) continue;

            // First column is timestamp, second is key
            DateTime? timestamp = null;
            if (row[0].ValueKind == JsonValueKind.String)
            {
                var tsStr = row[0].GetString();
                if (DateTime.TryParse(tsStr, out var ts))
                {
                    timestamp = ts;
                }
            }

            var key = row[1].ValueKind == JsonValueKind.String
                ? row[1].GetString()
                : null;

            if (!string.IsNullOrEmpty(key) && timestamp.HasValue)
            {
                result[key] = timestamp.Value;
            }
        }

        return result;
    }

    #endregion
}
