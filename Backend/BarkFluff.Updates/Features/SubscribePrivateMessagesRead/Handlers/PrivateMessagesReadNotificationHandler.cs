namespace BarkFluff.Updates.Features.SubscribePrivateMessagesRead.Handlers;

using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Proto.Updates;

using MediatR;

public class PrivateMessagesReadNotificationHandler : INotificationHandler<PrivateMessagesReadNotification>
{
    private readonly StreamSubscriptionsManager _subscriptionsManager;
    private readonly ILogger<PrivateMessagesReadNotificationHandler> _logger;
    private readonly MetricsCollector _metrics;

    public PrivateMessagesReadNotificationHandler(
        StreamSubscriptionsManager subscriptionsManager,
        ILogger<PrivateMessagesReadNotificationHandler> logger,
        MetricsCollector metrics)
    {
        _subscriptionsManager = subscriptionsManager;
        _logger = logger;
        _metrics = metrics;
    }

    public async Task Handle(PrivateMessagesReadNotification notification, CancellationToken cancellationToken)
    {
        var sendTasks = new List<Task>();
        foreach (var memberId in notification.Members)
        {
            foreach (var stream in _subscriptionsManager.GetUserStreams(memberId))
            {
                sendTasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        await stream.WriteAsync(new PrivateMessagesReadEvent
                        {
                            ChatId = notification.ChatId.ToString(),
                            UserId = notification.UserId,
                            LastReadMessageId = notification.LastReadMessageId,
                        }, cancellationToken);
                        _metrics.Increment("private_messages_read_broadcast");
                    }
                    catch (Exception ex)
                    {
                        _metrics.Increment("private_messages_read_broadcast_errors");
                        _logger.LogWarning(ex,
                            "Не удалось отправить read-state приватного чата {ChatId} пользователю {UserId}",
                            notification.ChatId, memberId);
                    }
                }, cancellationToken));
            }
        }

        await Task.WhenAll(sendTasks);
    }
}
