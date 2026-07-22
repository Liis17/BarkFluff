using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Onliner.Persistence.Contexts;
using BarkFluff.Onliner.Services;

using Microsoft.EntityFrameworkCore;

namespace BarkFluff.Onliner.BackgroundServices;

/// <summary>
/// Single-runner персист онлайн last-seen в БД раз в 10 минут: один инстанс (Redis-лидер) читает
/// снимок онлайн-пользователей из общего presence-стора и апсертит их last-seen в БД. Offline
/// last-seen пишет OfflineDetectionService в момент перехода. Апсерт идемпотентен.
/// </summary>
public class DatabasePersistenceService : BackgroundService
{
    private const string LockKey = "onliner:lock:db-persistence";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IPresenceStore _presence;
    private readonly RedisSingleRunner _singleRunner;
    private readonly MetricsCollector _metrics;
    private readonly ILogger<DatabasePersistenceService> _logger;

    private static readonly TimeSpan SaveInterval = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan LockTtl = TimeSpan.FromMinutes(5);

    public DatabasePersistenceService(
        IServiceScopeFactory scopeFactory,
        IPresenceStore presence,
        RedisSingleRunner singleRunner,
        MetricsCollector metrics,
        ILogger<DatabasePersistenceService> logger)
    {
        _scopeFactory = scopeFactory;
        _presence = presence;
        _singleRunner = singleRunner;
        _metrics = metrics;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Database Persistence Service started. Save interval: {Interval} minutes",
            SaveInterval.TotalMinutes);

        using var timer = new PeriodicTimer(SaveInterval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            _metrics.Increment("db_persistence_runs");
            try
            {
                // Single-runner: снапшот в БД пишет один инстанс за тик.
                if (!await _singleRunner.TryAcquireAsync(LockKey, LockTtl))
                {
                    continue;
                }

                await SaveStatusesToDatabaseAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _metrics.Increment("db_persistence_errors");
                _logger.LogError(ex, "Error during database persistence cycle");
            }
        }

        _logger.LogInformation("Database Persistence Service stopped");
    }

    private async Task SaveStatusesToDatabaseAsync(CancellationToken cancellationToken)
    {
        var onlineStatuses = await _presence.GetOnlineSnapshotAsync(cancellationToken);

        if (onlineStatuses.Count == 0)
        {
            _logger.LogDebug("No online statuses to save to database");
            return;
        }

        _logger.LogInformation("Saving {Count} online statuses to database", onlineStatuses.Count);

        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OnlineStatusContext>();

        foreach (var status in onlineStatuses)
        {
            var existing = await dbContext.UsersOnlineStatuses
                .FirstOrDefaultAsync(s => s.UserId == status.UserId, cancellationToken);

            if (existing != null)
            {
                dbContext.Entry(existing).CurrentValues.SetValues(status);
            }
            else
            {
                dbContext.UsersOnlineStatuses.Add(status);
            }
        }

        var savedCount = await dbContext.SaveChangesAsync(cancellationToken);
        _metrics.Add("db_records_saved_total", savedCount);
        _logger.LogInformation("Successfully saved {Count} status records to database", savedCount);
    }
}
