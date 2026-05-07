using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Onliner.Services;

namespace BarkFluff.Onliner.BackgroundServices;

/// <summary>
/// Background service для определения offline пользователей
/// Проверяет каждую секунду, если LastSeen > 5 секунд назад -> Offline
/// </summary>
public class OfflineDetectionService : BackgroundService
{
    private readonly OnlineStatusStorage _storage;
    private readonly OnlineStatusNotifier _notifier;
    private readonly ILogger<OfflineDetectionService> _logger;
    private readonly MetricsCollector _metrics;

    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan OfflineThreshold = TimeSpan.FromSeconds(5);

    public OfflineDetectionService(
        OnlineStatusStorage storage,
        OnlineStatusNotifier notifier,
        ILogger<OfflineDetectionService> logger,
        MetricsCollector metrics)
    {
        _storage = storage;
        _notifier = notifier;
        _logger = logger;
        _metrics = metrics;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Offline Detection Service started. Check interval: {Interval}s, Offline threshold: {Threshold}s",
            CheckInterval.TotalSeconds, OfflineThreshold.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckAndUpdateOfflineStatusesAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during offline detection cycle");
            }

            await Task.Delay(CheckInterval, stoppingToken);
        }

        _logger.LogInformation("Offline Detection Service stopped");
    }

    private async Task CheckAndUpdateOfflineStatusesAsync(CancellationToken cancellationToken)
    {
        // Получаем список пользователей которые Online, но LastSeen > 5 секунд назад
        var usersToMarkOffline = _storage.GetOnlineUsersOlderThan(OfflineThreshold);

        if (usersToMarkOffline.Count == 0)
        {
            return; // Нет пользователей для обновления
        }

        _logger.LogDebug("Found {Count} users to mark as offline", usersToMarkOffline.Count);

        foreach (var userId in usersToMarkOffline)
        {
            // Устанавливаем Offline статус
            bool statusChanged = _storage.SetOffline(userId);

            if (statusChanged)
            {
                _metrics.Increment("status_changes.offline");
                // Получаем обновленный статус для уведомления
                var status = _storage.GetStatus(userId);

                if (status != null)
                {
                    _logger.LogTrace("User {UserId} marked as offline", userId);

                    // Уведомляем подписчиков
                    await _notifier.NotifyStatusChanged(userId, status, cancellationToken);
                }
            }
        }
    }
}
