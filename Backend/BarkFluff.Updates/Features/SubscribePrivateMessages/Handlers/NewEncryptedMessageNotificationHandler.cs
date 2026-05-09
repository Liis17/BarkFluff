namespace BarkFluff.Updates.Features.SubscribePrivateMessages.Handlers;

using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Proto.Updates;

using MediatR;

public class NewEncryptedMessageNotificationHandler : INotificationHandler<NewEncryptedMessageNotification>
{
    private readonly StreamSubscriptionsManager _subscriptionsManager;
    private readonly ILogger<NewEncryptedMessageNotificationHandler> _logger;
    private readonly MetricsCollector _metrics;

    public NewEncryptedMessageNotificationHandler(
        StreamSubscriptionsManager subscriptionsManager,
        ILogger<NewEncryptedMessageNotificationHandler> logger,
        MetricsCollector metrics)
    {
        _subscriptionsManager = subscriptionsManager;
        _logger = logger;
        _metrics = metrics;
    }

    public async Task Handle(NewEncryptedMessageNotification notification, CancellationToken cancellationToken)
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
                        var evt = new NewEncryptedMessageEvent
                        {
                            ChatId = notification.ChatId.ToString(),
                            Message = notification.Message
                        };
                        await stream.WriteAsync(evt, cancellationToken);
                        _metrics.Increment("private_messages_broadcast");
                    }
                    catch (Exception ex)
                    {
                        _metrics.Increment("private_messages_broadcast_errors");
                        _logger.LogWarning(ex,
                            "Не удалось отправить шифрованное сообщение {MessageId} пользователю {UserId}",
                            notification.Message.Id, memberId);
                    }
                }, cancellationToken));
            }
        }

        await Task.WhenAll(sendTasks);
    }
}
