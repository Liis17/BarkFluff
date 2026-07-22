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

        var status = new UserOnlineStatus
        {
            UserId = msg.UserId,
            Status = (StatusTypeId)msg.Status,
            LastSeen = msg.LastSeen,
        };

        return notifier.NotifyStatusChanged(msg.UserId, status, context.CancellationToken);
    }
}
