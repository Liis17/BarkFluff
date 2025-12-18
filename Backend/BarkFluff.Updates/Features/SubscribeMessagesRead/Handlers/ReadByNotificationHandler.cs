using MediatR;
using Microsoft.Extensions.Logging;

namespace BarkFluff.Updates.Features.SubscribeMessagesRead.Handlers;

public class ReadByNotificationHandler : INotificationHandler<ReadByNotification>
{
    private readonly StreamSubscriptionsManager _subscriptionsManager;
    private readonly ILogger<ReadByNotificationHandler> _logger;

    public ReadByNotificationHandler(StreamSubscriptionsManager subscriptionsManager, ILogger<ReadByNotificationHandler> logger)
    {
        _subscriptionsManager = subscriptionsManager;
        _logger = logger;
    }

    public async Task Handle(ReadByNotification notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Обработка события прочтения сообщения. ChatId: {ChatId}, MessageId: {MessageId}, Прочитали: {ReadByCount} пользователей",
            notification.ChatId,
            notification.MessageId,
            notification.NewReadBy.Count()
        );

        int successfulStreams = 0;
        int failedStreams = 0;

        // Отправляем уведомление всем пользователям из списка Members
        foreach (var memberId in notification.ChatMembers)
        {
            var streams = _subscriptionsManager.GetUserStreams(memberId);

            _logger.LogDebug(
                "Отправка события прочтения пользователю {UserId}. Активных потоков: {StreamCount}",
                memberId,
                streams.Count()
            );

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
                    successfulStreams++;
                }
                catch (Exception ex)
                {
                    failedStreams++;
                    _logger.LogWarning(
                        ex,
                        "Ошибка при отправке события прочтения пользователю {UserId}. ChatId: {ChatId}, MessageId: {MessageId}",
                        memberId,
                        notification.ChatId,
                        notification.MessageId
                    );
                    // Если произошла ошибка при записи в поток, игнорируем и продолжаем
                    // Отключение подписки произойдет в gRPC сервисе при отмене запроса
                }
            }
        }

        _logger.LogInformation(
            "Событие прочтения обработано. ChatId: {ChatId}, MessageId: {MessageId}, Успешно: {SuccessCount}, Ошибок: {FailCount}",
            notification.ChatId,
            notification.MessageId,
            successfulStreams,
            failedStreams
        );
    }
}