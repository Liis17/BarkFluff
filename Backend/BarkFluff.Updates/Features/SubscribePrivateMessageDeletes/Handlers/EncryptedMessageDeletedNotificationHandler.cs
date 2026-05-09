namespace BarkFluff.Updates.Features.SubscribePrivateMessageDeletes.Handlers;

using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Proto.Updates;

using MediatR;

public class EncryptedMessageDeletedNotificationHandler : INotificationHandler<EncryptedMessageDeletedNotification>
{
    private readonly StreamSubscriptionsManager _subscriptionsManager;
    private readonly ILogger<EncryptedMessageDeletedNotificationHandler> _logger;
    private readonly MetricsCollector _metrics;

    public EncryptedMessageDeletedNotificationHandler(
        StreamSubscriptionsManager subscriptionsManager,
        ILogger<EncryptedMessageDeletedNotificationHandler> logger,
        MetricsCollector metrics)
    {
        _subscriptionsManager = subscriptionsManager;
        _logger = logger;
        _metrics = metrics;
    }

    public async Task Handle(EncryptedMessageDeletedNotification notification, CancellationToken cancellationToken)
    {
        var sendTasks = new List<Task>();

        foreach (var memberId in notification.Members)
        {
            var streams = _subscriptionsManager.GetUserStreams(memberId);
            foreach (var stream in streams)
            {
                sendTasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        var evt = new EncryptedMessageDeletedEvent
                        {
                            ChatId = notification.ChatId.ToString(),
                            MessageId = notification.MessageId
                        };
                        await stream.WriteAsync(evt, cancellationToken);
                        _metrics.Increment("private_messages_deleted_broadcast");
                    }
                    catch (Exception ex)
                    {
                        _metrics.Increment("private_messages_deleted_broadcast_errors");
                        _logger.LogWarning(ex,
                            "Не удалось отправить delete-событие шифрованного сообщения {MessageId} пользователю {UserId}",
                            notification.MessageId, memberId);
                    }
                }, cancellationToken));
            }
        }

        await Task.WhenAll(sendTasks);
    }
}
