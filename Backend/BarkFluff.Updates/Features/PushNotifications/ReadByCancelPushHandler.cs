using BarkFluff.Updates.Features.SubscribeMessagesRead;

using MediatR;

namespace BarkFluff.Updates.Features.PushNotifications;

/// <summary>
/// Обработчик событий прочтения сообщений.
/// Отменяет запланированные push-уведомления для пользователей, которые прочитали сообщение.
/// </summary>
public class ReadByCancelPushHandler : INotificationHandler<ReadByNotification>
{
    private readonly PendingPushTracker _pendingPushTracker;
    private readonly ILogger<ReadByCancelPushHandler> _logger;

    public ReadByCancelPushHandler(
        PendingPushTracker pendingPushTracker,
        ILogger<ReadByCancelPushHandler> logger)
    {
        _pendingPushTracker = pendingPushTracker;
        _logger = logger;
    }

    public Task Handle(ReadByNotification notification, CancellationToken cancellationToken)
    {
        foreach (var userId in notification.ReadersWithNewRead)
        {
            _pendingPushTracker.CancelPending(notification.MessageId, userId);

            _logger.LogDebug(
                "Отменено push-уведомление для сообщения {MessageId} пользователю {UserId}",
                notification.MessageId,
                userId);
        }

        return Task.CompletedTask;
    }
}
