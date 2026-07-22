using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Onliner.Domain.Entities;
using BarkFluff.Onliner.Domain.Enums;
using BarkFluff.Onliner.Messages;
using BarkFluff.Onliner.Persistence.Contexts;
using BarkFluff.Onliner.Services;

using MassTransit;

using Microsoft.EntityFrameworkCore;

namespace BarkFluff.Onliner.BackgroundServices;

/// <summary>
/// Single-runner детектор оффлайна: раз в секунду один инстанс (Redis-лидер) находит в общем
/// presence-сторе пользователей с последним heartbeat старше порога, снимает их с онлайна,
/// сохраняет offline last-seen в БД и публикует fan-out событие подписчикам.
/// Атомарный ZREM в SetOfflineAsync дополнительно гарантирует ровно-однократный offline-переход
/// даже при кратковременном двойном лидерстве (см. docs/scaling/onliner.md).
/// </summary>
public class OfflineDetectionService : BackgroundService
{
    private const string LockKey = "onliner:lock:offline-detection";

    private readonly IPresenceStore _presence;
    private readonly RedisSingleRunner _singleRunner;
    private readonly IBus _bus;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OfflineDetectionService> _logger;
    private readonly MetricsCollector _metrics;

    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan OfflineThreshold = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan LockTtl = TimeSpan.FromSeconds(3);

    public OfflineDetectionService(
        IPresenceStore presence,
        RedisSingleRunner singleRunner,
        IBus bus,
        IServiceScopeFactory scopeFactory,
        ILogger<OfflineDetectionService> logger,
        MetricsCollector metrics)
    {
        _presence = presence;
        _singleRunner = singleRunner;
        _bus = bus;
        _scopeFactory = scopeFactory;
        _logger = logger;
        _metrics = metrics;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Offline Detection Service started. Check interval: {Interval}s, Offline threshold: {Threshold}s",
            CheckInterval.TotalSeconds, OfflineThreshold.TotalSeconds);

        using var timer = new PeriodicTimer(CheckInterval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            _metrics.Increment("offline_detection_runs");
            try
            {
                // Single-runner: оффлайн-проход делает один инстанс, остальные пропускают тик.
                if (!await _singleRunner.TryAcquireAsync(LockKey, LockTtl))
                {
                    continue;
                }

                await CheckAndUpdateOfflineStatusesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _metrics.Increment("offline_detection_errors");
                _logger.LogError(ex, "Error during offline detection cycle");
            }
        }

        _logger.LogInformation("Offline Detection Service stopped");
    }

    private async Task CheckAndUpdateOfflineStatusesAsync(CancellationToken cancellationToken)
    {
        var stale = await _presence.GetStaleUsersAsync(OfflineThreshold, cancellationToken);

        if (stale.Count == 0)
        {
            return;
        }

        _logger.LogDebug("Found {Count} users to mark as offline", stale.Count);

        foreach (var candidate in stale)
        {
            // ZREM атомарен — true получит ровно один инстанс, он и уведомит/сохранит.
            if (!await _presence.SetOfflineAsync(candidate.UserId, cancellationToken))
            {
                continue;
            }

            _metrics.Increment("status_changes.offline");

            await PersistOfflineAsync(candidate.UserId, candidate.LastSeen, cancellationToken);

            _logger.LogTrace("User {UserId} marked as offline", candidate.UserId);

            await _bus.Publish(new OnlineStatusChangedEvent
            {
                UserId = candidate.UserId,
                Status = (int)StatusTypeId.Offline,
                LastSeen = candidate.LastSeen,
            }, cancellationToken);
        }
    }

    /// <summary>Сохранить offline last-seen в БД (в Redis offline-пользователей нет — снимаем ZREM'ом).</summary>
    private async Task PersistOfflineAsync(long userId, DateTime lastSeen, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OnlineStatusContext>();

        var existing = await db.UsersOnlineStatuses
            .FirstOrDefaultAsync(s => s.UserId == userId, cancellationToken);

        var offline = new UserOnlineStatus
        {
            UserId = userId,
            Status = StatusTypeId.Offline,
            LastSeen = lastSeen,
        };

        if (existing != null)
        {
            db.Entry(existing).CurrentValues.SetValues(offline);
        }
        else
        {
            db.UsersOnlineStatuses.Add(offline);
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
