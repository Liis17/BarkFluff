using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Onliner.Services;

using StackExchange.Redis;

namespace BarkFluff.Onliner.BackgroundServices;

/// <summary>
/// Периодически снимает текущие значения gauge-метрик из in-memory сервисов
/// и кладёт их в MetricsCollector через Set(). Запускается чаще, чем
/// MetricsReporterService публикует логи (5 сек), чтобы значения всегда
/// были актуальными к моменту репорта.
/// </summary>
public class MetricsSnapshotService : BackgroundService
{
    private readonly IPresenceStore _presence;
    private readonly OnlineStatusSubscriptionsManager _subscriptionsManager;
    private readonly MetricsCollector _metrics;
    private readonly ILogger<MetricsSnapshotService> _logger;

    private static readonly TimeSpan SnapshotInterval = TimeSpan.FromSeconds(2);

    public MetricsSnapshotService(
        IPresenceStore presence,
        OnlineStatusSubscriptionsManager subscriptionsManager,
        MetricsCollector metrics,
        ILogger<MetricsSnapshotService> logger)
    {
        _presence = presence;
        _subscriptionsManager = subscriptionsManager;
        _metrics = metrics;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Подписки — per-instance (локальный менеджер); online_users_count — глобальный (из Redis),
                // одинаков на всех инстансах: на дашбордах агрегировать max/avg, не sum.
                _metrics.Set("active_subscriptions", _subscriptionsManager.GetActiveSubscriptionsCount());
                _metrics.Set("tracked_unique_users", _subscriptionsManager.GetTrackedUniqueUsersCount());
                _metrics.Set("remote_tracked_uuids", _subscriptionsManager.GetTrackedUniqueUuidsCount());
                _metrics.Set("online_users_count", (int)await _presence.GetOnlineCountAsync(stoppingToken));
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (RedisException ex)
            {
                _logger.LogWarning(ex,
                    "Не удалось получить количество онлайн-пользователей из Redis; снимок online_users_count пропущен");
            }
            catch (TimeoutException ex)
            {
                _logger.LogWarning(ex,
                    "Таймаут при получении количества онлайн-пользователей из Redis; снимок online_users_count пропущен");
            }

            try
            {
                await Task.Delay(SnapshotInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
