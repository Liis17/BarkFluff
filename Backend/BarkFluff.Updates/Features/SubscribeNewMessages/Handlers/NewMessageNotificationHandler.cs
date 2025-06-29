namespace BarkFluff.Updates.Features.SubscribeNewMessages.Handlers;

using System.Threading;
using System.Threading.Tasks;
using BarkFluff.Proto.Updates;
using BarkFluff.Updates.Features.SubscribeNewMessages;
using MediatR;

public class NewMessageNotificationHandler : INotificationHandler<NewMessageNotification>
{
    private readonly StreamSubscriptionsManager _subscriptionsManager;

    public NewMessageNotificationHandler(StreamSubscriptionsManager subscriptionsManager)
    {
        _subscriptionsManager = subscriptionsManager;
    }

    public async Task Handle(NewMessageNotification notification, CancellationToken cancellationToken)
    {
        var message = notification.Message;
        
        // Отправляем уведомление всем пользователям из списка Members
        foreach (var memberId in notification.Members)
        {
            var streams = _subscriptionsManager.GetUserStreams(memberId);
            
            foreach (var stream in streams)
            {
                try
                {
                    var newMessageEvent = new NewMessageEvent
                    {
                        Message = message, ChatId = notification.ChatId.ToString()
                    };
                    await stream.WriteAsync(newMessageEvent, cancellationToken);
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
