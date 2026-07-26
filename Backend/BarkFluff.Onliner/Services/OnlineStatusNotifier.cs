using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Proto.Onliner;

using Google.Protobuf.WellKnownTypes;

namespace BarkFluff.Onliner.Services;

/// <summary>
/// Сервис для уведомления подписчиков об изменениях статусов
/// Singleton, инъектируется в handlers и background services
/// </summary>
public class OnlineStatusNotifier
{
    private readonly OnlineStatusSubscriptionsManager _subscriptionsManager;
    private readonly MetricsCollector _metrics;
    private readonly ILogger<OnlineStatusNotifier> _logger;

    public OnlineStatusNotifier(
        OnlineStatusSubscriptionsManager subscriptionsManager,
        MetricsCollector metrics,
        ILogger<OnlineStatusNotifier> logger)
    {
        _subscriptionsManager = subscriptionsManager;
        _metrics = metrics;
        _logger = logger;
    }

    /// <summary>
    /// Уведомить всех подписчиков об изменении статуса
    /// </summary>
    public async Task NotifyStatusChanged(
        long userId,
        Domain.Entities.UserOnlineStatus status,
        CancellationToken cancellationToken = default)
    {
        var streams = _subscriptionsManager.GetStreamsTrackingUser(userId);

        if (streams.Count == 0)
        {
            _logger.LogTrace("No subscribers for user {UserId} status change", userId);
            return;
        }

        var protoStatus = new UserOnlineStatus
        {
            UserId = status.UserId,
            Status = MapStatus(status.Status),
            LastSeen = Timestamp.FromDateTime(status.LastSeen.ToUniversalTime())
        };

        _logger.LogDebug(
            "Notifying {StreamCount} subscribers about user {UserId} status change to {Status}",
            streams.Count, userId, status.Status);

        // Отправляем уведомления параллельно
        var tasks = streams.Select(stream => SendToStreamAsync(stream, protoStatus, userId, cancellationToken));
        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// Уведомить подписчиков об изменении статуса remote-пользователя (этап 4.2).
    /// У него нет локального long-идентификатора, поэтому адресация — по UUID,
    /// а <c>user_id</c> в событии остаётся нулевым.
    /// </summary>
    public async Task NotifyRemoteStatusChanged(
        Guid userUuid,
        Domain.Enums.StatusTypeId status,
        DateTime lastSeen,
        CancellationToken cancellationToken = default)
    {
        var streams = _subscriptionsManager.GetStreamsTrackingUuid(userUuid);

        if (streams.Count == 0)
        {
            _logger.LogTrace("No subscribers for remote user {UserUuid} status change", userUuid);
            return;
        }

        var protoStatus = new UserOnlineStatus
        {
            UserUuid = userUuid.ToString(),
            Status = MapStatus(status),
            LastSeen = Timestamp.FromDateTime(lastSeen.ToUniversalTime())
        };

        _logger.LogDebug(
            "Notifying {StreamCount} subscribers about remote user {UserUuid} status change to {Status}",
            streams.Count, userUuid, status);

        var tasks = streams.Select(stream => SendToStreamAsync(stream, protoStatus, 0, cancellationToken));
        await Task.WhenAll(tasks);
    }

    private async Task SendToStreamAsync(
        Grpc.Core.IServerStreamWriter<UserOnlineStatus> stream,
        UserOnlineStatus status,
        long userId,
        CancellationToken cancellationToken)
    {
        try
        {
            await stream.WriteAsync(status, cancellationToken);
            _metrics.Increment("status_notifications_sent");
            _logger.LogTrace("Successfully sent status update for user {UserId}", userId);
        }
        catch (Exception ex)
        {
            // Stream может быть закрыт - это нормально, cleanup произойдет в gRPC service
            _metrics.Increment("status_notification_errors");
            _logger.LogWarning(ex,
                "Failed to send status update for user {UserId} - stream likely disconnected",
                userId);
        }
    }

    private static StatusTypeId MapStatus(Domain.Enums.StatusTypeId domainStatus)
    {
        return domainStatus switch
        {
            Domain.Enums.StatusTypeId.Online => StatusTypeId.StatusOnline,
            Domain.Enums.StatusTypeId.Offline => StatusTypeId.StatusOffline,
            _ => StatusTypeId.Unknown
        };
    }
}
