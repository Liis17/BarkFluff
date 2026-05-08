using Barkfluff.AdminPanel.Models;

using System.Collections.Concurrent;

namespace Barkfluff.AdminPanel.Services;

public class LogsClearService : IDisposable
{
    private const int OldLogsThresholdDays = 14;
    private static readonly TimeSpan TtlAfterDone = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromMinutes(5);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<LogsClearService> _logger;
    private readonly ConcurrentDictionary<Guid, LogsClearJob> _jobs = new();
    private readonly Timer _cleanupTimer;

    public LogsClearService(IServiceScopeFactory scopeFactory, ILogger<LogsClearService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _cleanupTimer = new Timer(_ => CleanupExpiredJobs(), null, CleanupInterval, CleanupInterval);
    }

    public Guid StartClear(LogsClearScope scope)
    {
        var jobId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var job = new LogsClearJob
        {
            Id = jobId,
            Scope = scope,
            State = LogsClearState.Queued,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        _jobs[jobId] = job;

        _ = Task.Run(() => RunClearAsync(job));

        return jobId;
    }

    public LogsClearJob? GetJob(Guid jobId)
        => _jobs.TryGetValue(jobId, out var job) ? job : null;

    private async Task RunClearAsync(LogsClearJob job)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var seq = scope.ServiceProvider.GetRequiredService<SeqService>();

            DateTime? toDateUtc = job.Scope == LogsClearScope.Old
                ? DateTime.UtcNow.AddDays(-OldLogsThresholdDays)
                : null;

            // Stage 1: count
            job.State = LogsClearState.Counting;
            job.UpdatedAtUtc = DateTime.UtcNow;

            var total = await seq.CountEventsAsync(fromDateUtc: null, toDateUtc: toDateUtc);
            job.TotalCount = total ?? 0;
            job.UpdatedAtUtc = DateTime.UtcNow;

            // Stage 2: delete
            job.State = LogsClearState.Deleting;
            job.UpdatedAtUtc = DateTime.UtcNow;

            var deleted = await seq.DeleteEventsAsync(fromDateUtc: null, toDateUtc: toDateUtc);
            job.DeletedCount = deleted ?? job.TotalCount;
            job.State = LogsClearState.Done;
            job.UpdatedAtUtc = DateTime.UtcNow;

            _logger.LogInformation(
                "Logs clear {JobId} done: {Deleted} of {Total} events (scope={Scope})",
                job.Id, job.DeletedCount, job.TotalCount, job.Scope);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Logs clear failed: {JobId}", job.Id);
            job.Error = ex.Message;
            job.State = LogsClearState.Error;
            job.UpdatedAtUtc = DateTime.UtcNow;
        }
    }

    private void CleanupExpiredJobs()
    {
        var now = DateTime.UtcNow;
        foreach (var kv in _jobs)
        {
            var job = kv.Value;
            if (job.State is LogsClearState.Done or LogsClearState.Error
                && now - job.UpdatedAtUtc > TtlAfterDone)
            {
                if (_jobs.TryRemove(kv.Key, out var removed))
                {
                    _logger.LogInformation("Cleaning up expired logs clear job {JobId}", removed.Id);
                }
            }
        }
    }

    public void Dispose()
    {
        _cleanupTimer.Dispose();
    }
}
