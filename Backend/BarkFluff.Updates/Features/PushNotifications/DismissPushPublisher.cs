using BarkFluff.Shared.Queue.Messages;
using BarkFluff.Updates.Features.SubscribeMessagesRead;

using MassTransit;

using MediatR;

namespace BarkFluff.Updates.Features.PushNotifications;

/// <summary>
/// Публикует <see cref="DismissPushEvent"/> для каждого прочитавшего пользователя,
/// чтобы CloudMessaging скрыл нотификацию чата на остальных его устройствах.
/// Работает параллельно с <see cref="ReadByCancelPushHandler"/>: тот отменяет ещё не отправленные push,
/// а этот гасит уже доставленные.
/// </summary>
public class DismissPushPublisher : INotificationHandler<ReadByNotification>
{
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly DismissPushDebouncer _debouncer;
    private readonly ILogger<DismissPushPublisher> _logger;

    public DismissPushPublisher(
        IPublishEndpoint publishEndpoint,
        DismissPushDebouncer debouncer,
        ILogger<DismissPushPublisher> logger)
    {
        _publishEndpoint = publishEndpoint;
        _debouncer = debouncer;
        _logger = logger;
    }

    public async Task Handle(ReadByNotification notification, CancellationToken cancellationToken)
    {
        var publishTasks = notification.NewReadBy
            .Distinct()
            .Select(userId => _debouncer.RunAsync(
                userId,
                notification.ChatId,
                async token =>
                {
                    await _publishEndpoint.Publish(
                        new DismissPushEvent
                        {
                            ChatId = notification.ChatId,
                            UserId = userId
                        },
                        token);

                    _logger.LogDebug(
                        "Опубликован dismiss push для UserId={UserId}, ChatId={ChatId}",
                        userId,
                        notification.ChatId);
                },
                cancellationToken));

        await Task.WhenAll(publishTasks);
    }
}
