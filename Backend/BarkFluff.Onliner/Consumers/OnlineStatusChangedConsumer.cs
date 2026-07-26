using BarkFluff.Onliner.Domain.Entities;
using BarkFluff.Onliner.Domain.Enums;
using BarkFluff.Onliner.Messages;
using BarkFluff.Onliner.Services;

using MassTransit;

namespace BarkFluff.Onliner.Consumers;

/// <summary>
/// Fan-out доставки изменения онлайн-статуса: каждый инстанс получает копию и уведомляет своих
/// локальных подписчиков (<see cref="OnlineStatusSubscriptionsManager"/>). Инстансы без
/// подписчика на этого пользователя — no-op.
/// </summary>
public class OnlineStatusChangedConsumer(OnlineStatusNotifier notifier) : IConsumer<OnlineStatusChangedEvent>
{
    public Task Consume(ConsumeContext<OnlineStatusChangedEvent> context)
    {
        var msg = context.Message;

        // Remote-пользователь (этап 4.2): локального UserId нет, адресуем по UUID.
        // Событие без нового поля (инстанс старой версии) идёт прежним путём.
        if (msg.UserUuid.HasValue)
        {
            return notifier.NotifyRemoteStatusChanged(
                msg.UserUuid.Value, (StatusTypeId)msg.Status, msg.LastSeen, context.CancellationToken);
        }

        var status = new UserOnlineStatus
        {
            UserId = msg.UserId,
            Status = (StatusTypeId)msg.Status,
            LastSeen = msg.LastSeen,
        };

        return notifier.NotifyStatusChanged(msg.UserId, status, context.CancellationToken);
    }
}
