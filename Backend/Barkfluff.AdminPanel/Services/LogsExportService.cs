using Barkfluff.AdminPanel.Models;

using System.Collections.Concurrent;
using System.IO.Compression;
using System.Text.Json;

namespace Barkfluff.AdminPanel.Services;

public class LogsExportService : IDisposable
{
    private const int PageSize = 1000;
    private const int OldLogsThresholdDays = 14;
    private static readonly TimeSpan TtlAfterReady = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromMinutes(5);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<LogsExportService> _logger;
    private readonly ConcurrentDictionary<Guid, LogsExportJob> _jobs = new();
    private readonly string _rootDir;
    private readonly Timer _cleanupTimer;

    public LogsExportService(IServiceScopeFactory scopeFactory, ILogger<LogsExportService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _rootDir = Path.Combine(Path.GetTempPath(), "logs-export");
        Directory.CreateDirectory(_rootDir);

        _cleanupTimer = new Timer(_ => CleanupExpiredJobs(), null, CleanupInterval, CleanupInterval);
    }

    public Guid StartExport(LogsExportScope scope)
    {
        var jobId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var job = new LogsExportJob
        {
            Id = jobId,
            Scope = scope,
            State = LogsExportState.Queued,
            TempDir = Path.Combine(_rootDir, jobId.ToString("N")),
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        _jobs[jobId] = job;

        _ = Task.Run(() => RunExportAsync(job));

        return jobId;
    }

    public LogsExportJob? GetJob(Guid jobId)
        => _jobs.TryGetValue(jobId, out var job) ? job : null;

    public bool TryDeleteJobFiles(Guid jobId)
    {
        if (!_jobs.TryRemove(jobId, out var job))
            return false;

        SafeCleanupFiles(job);
        return true;
    }

    private async Task RunExportAsync(LogsExportJob job)
    {
        try
        {
            Directory.CreateDirectory(job.TempDir);

            job.State = LogsExportState.Downloading;
            job.UpdatedAtUtc = DateTime.UtcNow;

            using var scope = _scopeFactory.CreateScope();
            var seq = scope.ServiceProvider.GetRequiredService<SeqService>();

            DateTime? toDateUtc = job.Scope == LogsExportScope.Old
                ? DateTime.UtcNow.AddDays(-OldLogsThresholdDays)
                : null;

            string? afterId = null;
            int pageNum = 0;

            while (true)
            {
                var resp = await seq.GetEventsAsync(
                    filter: null,
                    count: PageSize,
                    fromDateUtc: null,
                    afterId: afterId,
                    toDateUtc: toDateUtc);

                if (resp is null)
                {
                    throw new InvalidOperationException("Seq вернул пустой ответ (возможна ошибка соединения)");
                }

                var pageEvents = SeqService.ExtractEventsArray(resp.Value);
                if (pageEvents is null || pageEvents.Count == 0)
                    break;

                var pagePath = Path.Combine(job.TempDir, $"page-{pageNum:D5}.json");
                await using (var fs = new FileStream(pagePath, FileMode.Create, FileAccess.Write, FileShare.None))
                await using (var writer = new Utf8JsonWriter(fs, new JsonWriterOptions { Indented = false }))
                {
                    writer.WriteStartArray();
                    foreach (var ev in pageEvents)
                        ev.WriteTo(writer);
                    writer.WriteEndArray();
                }

                job.TotalDownloaded += pageEvents.Count;
                job.CurrentPage = pageNum;
                job.UpdatedAtUtc = DateTime.UtcNow;

                if (pageEvents.Count < PageSize)
                    break;

                afterId = GetEventId(pageEvents[^1]);
                if (afterId is null)
                    break;

                pageNum++;
            }

            job.State = LogsExportState.Compressing;
            job.UpdatedAtUtc = DateTime.UtcNow;

            var zipPath = Path.Combine(_rootDir, $"logs-export-{job.Id:N}.zip");
            if (File.Exists(zipPath))
                File.Delete(zipPath);

            ZipFile.CreateFromDirectory(job.TempDir, zipPath, CompressionLevel.Optimal, includeBaseDirectory: false);

            try { Directory.Delete(job.TempDir, recursive: true); } catch { /* ignore */ }

            job.ZipPath = zipPath;
            job.ZipSizeBytes = new FileInfo(zipPath).Length;
            job.State = LogsExportState.Ready;
            job.UpdatedAtUtc = DateTime.UtcNow;

            _logger.LogInformation(
                "Logs export {JobId} ready: {TotalDownloaded} events, {ZipSize} bytes",
                job.Id, job.TotalDownloaded, job.ZipSizeBytes);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Logs export failed: {JobId}", job.Id);
            job.Error = ex.Message;
            job.State = LogsExportState.Error;
            job.UpdatedAtUtc = DateTime.UtcNow;
            SafeCleanupFiles(job);
        }
    }

    private static string? GetEventId(JsonElement evt)
    {
        if (evt.ValueKind != JsonValueKind.Object) return null;
        if (evt.TryGetProperty("Id", out var id) && id.ValueKind == JsonValueKind.String)
            return id.GetString();
        if (evt.TryGetProperty("id", out var idLower) && idLower.ValueKind == JsonValueKind.String)
            return idLower.GetString();
        return null;
    }

    private void CleanupExpiredJobs()
    {
        var now = DateTime.UtcNow;
        foreach (var kv in _jobs)
        {
            var job = kv.Value;
            if (job.State is LogsExportState.Ready or LogsExportState.Error
                && now - job.UpdatedAtUtc > TtlAfterReady)
            {
                if (_jobs.TryRemove(kv.Key, out var removed))
                {
                    _logger.LogInformation("Cleaning up expired logs export job {JobId}", removed.Id);
                    SafeCleanupFiles(removed);
                }
            }
        }
    }

    private static void SafeCleanupFiles(LogsExportJob job)
    {
        try
        {
            if (!string.IsNullOrEmpty(job.ZipPath) && File.Exists(job.ZipPath))
                File.Delete(job.ZipPath);
        }
        catch { /* ignore */ }

        try
        {
            if (!string.IsNullOrEmpty(job.TempDir) && Directory.Exists(job.TempDir))
                Directory.Delete(job.TempDir, recursive: true);
        }
        catch { /* ignore */ }
    }

    public void Dispose()
    {
        _cleanupTimer.Dispose();
    }
}
