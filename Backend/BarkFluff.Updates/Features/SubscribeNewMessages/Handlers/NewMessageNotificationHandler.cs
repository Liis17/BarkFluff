namespace BarkFluff.Updates.Features.SubscribeNewMessages.Handlers;

using System.Threading;
using System.Threading.Tasks;
using BarkFluff.Proto.Updates;
using BarkFluff.Updates.Features.SubscribeNewMessages;
using MediatR;
using Microsoft.Extensions.Logging;

public class NewMessageNotificationHandler : INotificationHandler<NewMessageNotification>
{
    private readonly StreamSubscriptionsManager _subscriptionsManager;
    private readonly ILogger<NewMessageNotificationHandler> _logger;

    public NewMessageNotificationHandler(
        StreamSubscriptionsManager subscriptionsManager,
        ILogger<NewMessageNotificationHandler> logger)
    {
        _subscriptionsManager = subscriptionsManager;
        _logger = logger;
    }

    public async Task Handle(NewMessageNotification notification, CancellationToken cancellationToken)
    {
        var message = notification.Message;
        
        _logger.LogDebug("Processing new message notification for chat {ChatId} with {MemberCount} members", 
            notification.ChatId, notification.Members.Count);
        
        // Собираем все задачи отправки и выполняем параллельно для ускорения
        var sendTasks = new List<Task>();
        
        // Отправляем уведомление всем пользователям из списка Members
        foreach (var memberId in notification.Members)
        {
            var streams = _subscriptionsManager.GetUserStreams(memberId);
            
            foreach (var stream in streams)
            {
                sendTasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        var newMessageEvent = new NewMessageEvent
                        {
                            Message = message, ChatId = notification.ChatId.ToString()
                        };
                        await stream.WriteAsync(newMessageEvent, cancellationToken);
                        
                        _logger.LogDebug("Successfully sent message {MessageId} to user {UserId}", 
                            message.Id, memberId);
                    }
                    catch (Exception ex)
                    {
                        // Если произошла ошибка при записи в поток, логируем и продолжаем
                        // Отключение подписки произойдет в gRPC сервисе при отмене запроса
                        _logger.LogWarning(ex, "Failed to send message {MessageId} to user {UserId} stream", 
                            message.Id, memberId);
                    }
                }, cancellationToken));
            }
        }
        
        await Task.WhenAll(sendTasks);
    }
}
