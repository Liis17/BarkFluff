using MediatR;

namespace BarkFluff.Updates.Features.SubscribeMessagesRead.Handlers;

public class ReadByNotificationHandler : INotificationHandler<ReadByNotification>
{
    private readonly StreamSubscriptionsManager _subscriptionsManager;

    public ReadByNotificationHandler(StreamSubscriptionsManager subscriptionsManager)
    {
        _subscriptionsManager = subscriptionsManager;
    }

    public async Task Handle(ReadByNotification notification, CancellationToken cancellationToken)
    {
        
        // Отправляем уведомление всем пользователям из списка Members
        foreach (var memberId in notification.ChatMembers)
        {
            var streams = _subscriptionsManager.GetUserStreams(memberId);
            
            foreach (var stream in streams)
            {
                try
                {
                    var newReadEvent = new BarkFluff.Proto.Updates.MessageReadEvent()
                    {
                        ChatId = notification.ChatId.ToString(),
                        MessageId = notification.MessageId,
                        NewReadBy = { notification.NewReadBy },
                    };
                    await stream.WriteAsync(newReadEvent, cancellationToken);
                }
                catch
                {
                    // Если произошла ошибка при записи в поток, игнорируем и продолжаем
                    // Отключение подписки произойдет в gRPC сервисе при отмене запроса
                }
            }
        }
    }
}