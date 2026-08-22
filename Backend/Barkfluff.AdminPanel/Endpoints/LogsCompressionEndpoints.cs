using Barkfluff.AdminPanel.Data;
using Barkfluff.AdminPanel.Middleware;
using Barkfluff.AdminPanel.Models;
using Barkfluff.AdminPanel.Services;

namespace Barkfluff.AdminPanel.Endpoints;

public static class LogsCompressionEndpoints
{
    public static void MapLogsCompressionEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/seq/compress-metrics")
            .WithTags("LogsCompression")
            .RequirePermission(AdminPermissions.SeqDelete);

        // POST /api/seq/compress-metrics/run?date=YYYY-MM-DD
        // Без date — берётся вчерашний день UTC.
        group.MapPost("/run", async (
            MetricsLogCompressorService compressor,
            string? date,
            CancellationToken ct) =>
        {
            DateTime targetDay;
            if (!string.IsNullOrEmpty(date))
            {
                if (!DateTime.TryParse(date, null, System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var parsed))
                    return Results.BadRequest(new { error = $"Invalid date format: '{date}'. Expected YYYY-MM-DD." });
                targetDay = parsed.Date;
            }
            else
            {
                targetDay = DateTime.UtcNow.Date.AddDays(-1);
            }

            var run = await compressor.CompressDayAsync(targetDay, ct);
            if (run is null)
                return Results.StatusCode(502);

            return Results.Ok(new
            {
                dayUtc = run.DayUtc.ToString("yyyy-MM-dd"),
                completedAtUtc = run.CompletedAtUtc.ToString("o"),
                serviceCount = run.ServiceCount,
                sourceEventCount = run.SourceEventCount,
                deletedCount = run.DeletedCount,
                dryRun = run.DryRun
            });
        })
        .WithName("RunLogsCompression")
        .WithOpenApi();

        // GET /api/seq/compress-metrics/history?limit=30
        group.MapGet("/history", (
            MetricsCacheDbContext cache,
            int limit = 30) =>
        {
            var safeLimit = Math.Clamp(limit, 1, 365);
            var runs = cache.CompressionRuns
                .FindAll()
                .OrderByDescending(r => r.DayUtc)
                .Take(safeLimit)
                .Select(r => new
                {
                    dayUtc = r.DayUtc.ToString("yyyy-MM-dd"),
                    completedAtUtc = r.CompletedAtUtc.ToString("o"),
                    serviceCount = r.ServiceCount,
                    sourceEventCount = r.SourceEventCount,
                    deletedCount = r.DeletedCount,
                    dryRun = r.DryRun
                })
                .ToList();

            return Results.Ok(runs);
        })
        .WithName("GetLogsCompressionHistory")
        .WithOpenApi();
    }
}
