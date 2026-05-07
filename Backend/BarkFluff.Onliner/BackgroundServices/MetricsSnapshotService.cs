using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Onliner.Services;

namespace BarkFluff.Onliner.BackgroundServices;

/// <summary>
/// Периодически снимает текущие значения gauge-метрик из in-memory сервисов
/// и кладёт их в MetricsCollector через Set(). Запускается чаще, чем
/// MetricsReporterService публикует логи (5 сек), чтобы значения всегда
/// были актуальными к моменту репорта.
/// </summary>
public class MetricsSnapshotService : BackgroundService
{
    private readonly OnlineStatusStorage _storage;
    private readonly OnlineStatusSubscriptionsManager _subscriptionsManager;
    private readonly MetricsCollector _metrics;

    private static readonly TimeSpan SnapshotInterval = TimeSpan.FromSeconds(2);

    public MetricsSnapshotService(
        OnlineStatusStorage storage,
        OnlineStatusSubscriptionsManager subscriptionsManager,
        MetricsCollector metrics)
    {
        _storage = storage;
        _subscriptionsManager = subscriptionsManager;
        _metrics = metrics;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            _metrics.Set("active_subscriptions", _subscriptionsManager.GetActiveSubscriptionsCount());
            _metrics.Set("tracked_unique_users", _subscriptionsManager.GetTrackedUniqueUsersCount());
            _metrics.Set("online_users_count", _storage.GetOnlineCount());
            _metrics.Set("storage_total_count", _storage.GetTotalCount());

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
