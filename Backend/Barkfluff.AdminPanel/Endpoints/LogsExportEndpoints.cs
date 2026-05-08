using Barkfluff.AdminPanel.Models;
using Barkfluff.AdminPanel.Services;

namespace Barkfluff.AdminPanel.Endpoints;

public static class LogsExportEndpoints
{
    public record StartExportRequest(string Scope);

    public static void MapLogsExportEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/seq/export")
            .WithTags("LogsExport");

        // POST /api/seq/export/start
        group.MapPost("/start", (
            StartExportRequest request,
            LogsExportService exportService,
            HttpContext context) =>
        {
            if (context.Items["AuthToken"] is not AuthToken)
                return Results.Unauthorized();

            var scope = request.Scope?.Equals("old", StringComparison.OrdinalIgnoreCase) == true
                ? LogsExportScope.Old
                : LogsExportScope.All;

            var jobId = exportService.StartExport(scope);
            return Results.Ok(new { jobId });
        })
        .WithName("StartLogsExport")
        .WithOpenApi();

        // GET /api/seq/export/{jobId}/status
        group.MapGet("/{jobId:guid}/status", (
            Guid jobId,
            LogsExportService exportService,
            HttpContext context) =>
        {
            if (context.Items["AuthToken"] is not AuthToken)
                return Results.Unauthorized();

            var job = exportService.GetJob(jobId);
            if (job is null)
                return Results.NotFound();

            return Results.Ok(new
            {
                state = job.State.ToString().ToLowerInvariant(),
                scope = job.Scope.ToString().ToLowerInvariant(),
                totalDownloaded = job.TotalDownloaded,
                currentPage = job.CurrentPage,
                zipSizeBytes = job.ZipSizeBytes,
                error = job.Error
            });
        })
        .WithName("GetLogsExportStatus")
        .WithOpenApi();

        // GET /api/seq/export/{jobId}/download
        group.MapGet("/{jobId:guid}/download", (
            Guid jobId,
            LogsExportService exportService,
            HttpContext context) =>
        {
            if (context.Items["AuthToken"] is not AuthToken)
                return Results.Unauthorized();

            var job = exportService.GetJob(jobId);
            if (job is null || job.State != LogsExportState.Ready || string.IsNullOrEmpty(job.ZipPath) || !File.Exists(job.ZipPath))
                return Results.NotFound();

            var fileName = $"barkfluff-logs-{job.Scope.ToString().ToLowerInvariant()}-{DateTime.UtcNow:yyyyMMdd-HHmm}.zip";
            var fs = new FileStream(job.ZipPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);

            context.Response.OnCompleted(() =>
            {
                try { exportService.TryDeleteJobFiles(jobId); } catch { /* ignore */ }
                return Task.CompletedTask;
            });

            return Results.Stream(fs, "application/zip", fileName);
        })
        .WithName("DownloadLogsExport")
        .WithOpenApi();
    }
}
