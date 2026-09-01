namespace BarkFluff.Updates.Features.SubscribeNewMessages.Handlers;

using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Proto.Shared;
using BarkFluff.Proto.Updates;
using BarkFluff.Updates.Features.SubscribeNewMessages;

using MediatR;

using Microsoft.Extensions.Logging;

using System.Threading;
using System.Threading.Tasks;

public class NewMessageNotificationHandler : INotificationHandler<NewMessageNotification>
{
    private readonly StreamSubscriptionsManager _subscriptionsManager;
    private readonly ILogger<NewMessageNotificationHandler> _logger;
    private readonly MetricsCollector _metrics;

    public NewMessageNotificationHandler(
        StreamSubscriptionsManager subscriptionsManager,
        ILogger<NewMessageNotificationHandler> logger,
        MetricsCollector metrics)
    {
        _subscriptionsManager = subscriptionsManager;
        _logger = logger;
        _metrics = metrics;
    }

    public async Task Handle(NewMessageNotification notification, CancellationToken cancellationToken)
    {
        var message = notification.Message;

        _logger.LogDebug("Processing new message notification for chat {ChatId} with {MemberCount} members",
            notification.ChatId, notification.Members.Count);

        // Собираем все задачи отправки и выполняем параллельно для ускорения.
        // Запись каждой конкретной подписки сериализуется внутри менеджера.
        var sendTasks = new List<Task>();

        // Отправляем уведомление всем пользователям из списка Members
        foreach (var memberId in notification.Members)
        {
            var subscriptions = _subscriptionsManager.GetUserSubscriptions(memberId);

            foreach (var subscription in subscriptions)
            {
                sendTasks.Add(SendToSubscriptionAsync(subscription, memberId, message, notification.ChatId, cancellationToken));
            }
        }

        await Task.WhenAll(sendTasks);
    }

    private async Task SendToSubscriptionAsync(
        NewMessageStreamSubscription subscription,
        long userId,
        Message message,
        Guid chatId,
        CancellationToken cancellationToken)
    {
        try
        {
            await subscription.WriteAsync(new NewMessageEvent
            {
                Message = message,
                ChatId = chatId.ToString()
            }, cancellationToken);

            _metrics.Increment("new_messages_broadcast");
            _metrics.Increment("events_broadcast"); // обратная совместимость

            _logger.LogDebug("Successfully sent message {MessageId} to user {UserId}",
                message.Id, userId);
        }
        catch (Exception ex)
        {
            _metrics.Increment("new_messages_broadcast_errors");
            _metrics.Increment("events_broadcast_errors");
            // Если произошла ошибка при записи в поток, логируем и продолжаем
            // Отключение подписки произойдет в gRPC сервисе при отмене запроса
            _logger.LogWarning(ex, "Failed to send message {MessageId} to user {UserId} stream",
                message.Id, userId);
        }
    }
}
