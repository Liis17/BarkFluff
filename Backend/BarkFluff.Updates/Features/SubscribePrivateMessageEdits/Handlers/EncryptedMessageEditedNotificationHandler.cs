namespace BarkFluff.Updates.Features.SubscribePrivateMessageEdits.Handlers;

using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Proto.Updates;

using MediatR;

public class EncryptedMessageEditedNotificationHandler : INotificationHandler<EncryptedMessageEditedNotification>
{
    private readonly StreamSubscriptionsManager _subscriptionsManager;
    private readonly ILogger<EncryptedMessageEditedNotificationHandler> _logger;
    private readonly MetricsCollector _metrics;

    public EncryptedMessageEditedNotificationHandler(
        StreamSubscriptionsManager subscriptionsManager,
        ILogger<EncryptedMessageEditedNotificationHandler> logger,
        MetricsCollector metrics)
    {
        _subscriptionsManager = subscriptionsManager;
        _logger = logger;
        _metrics = metrics;
    }

    public async Task Handle(EncryptedMessageEditedNotification notification, CancellationToken cancellationToken)
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
                        var evt = new EncryptedMessageEditedEvent
                        {
                            ChatId = notification.ChatId.ToString(),
                            Message = notification.Message
                        };
                        await stream.WriteAsync(evt, cancellationToken);
                        _metrics.Increment("private_messages_edited_broadcast");
                    }
                    catch (Exception ex)
                    {
                        _metrics.Increment("private_messages_edited_broadcast_errors");
                        _logger.LogWarning(ex,
                            "Не удалось отправить edit-событие шифрованного сообщения {MessageId} пользователю {UserId}",
                            notification.Message.Id, memberId);
                    }
                }, cancellationToken));
            }
        }

        await Task.WhenAll(sendTasks);
    }
}
