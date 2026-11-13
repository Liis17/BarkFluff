namespace BarkFluff.Updates.Features.SubscribeChatFoldersChanged.Handlers;

using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Proto.Updates;

using MediatR;

using Microsoft.Extensions.Logging;

using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Рассылает сигнал об изменении папок всем открытым стримам пользователя (все его устройства).
/// </summary>
public class ChatFoldersChangedNotificationHandler : INotificationHandler<ChatFoldersChangedNotification>
{
    private static readonly ChatFoldersChangedEvent Signal = new();

    private readonly StreamSubscriptionsManager _subscriptionsManager;
    private readonly ILogger<ChatFoldersChangedNotificationHandler> _logger;
    private readonly MetricsCollector _metrics;

    public ChatFoldersChangedNotificationHandler(
        StreamSubscriptionsManager subscriptionsManager,
        ILogger<ChatFoldersChangedNotificationHandler> logger,
        MetricsCollector metrics)
    {
        _subscriptionsManager = subscriptionsManager;
        _logger = logger;
        _metrics = metrics;
    }

    public async Task Handle(ChatFoldersChangedNotification notification, CancellationToken cancellationToken)
    {
        var sendTasks = _subscriptionsManager.GetUserStreams(notification.UserId)
            .Select(async stream =>
            {
                try
                {
                    await stream.WriteAsync(Signal, cancellationToken);
                    _metrics.Increment("chat_folders_changed_broadcast");
                }
                catch (Exception ex)
                {
                    _metrics.Increment("chat_folders_changed_broadcast_errors");
                    _logger.LogWarning(ex, "Failed to send chat-folders-changed to user {UserId} stream",
                        notification.UserId);
                }
            });

        await Task.WhenAll(sendTasks);
    }
}
