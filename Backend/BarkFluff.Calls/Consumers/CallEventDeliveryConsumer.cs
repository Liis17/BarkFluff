using BarkFluff.Calls.Messages;
using BarkFluff.Calls.Services;
using BarkFluff.Proto.Calls;

using MassTransit;

namespace BarkFluff.Calls.Consumers;

/// <summary>
/// Fan-out доставка событий звонка: каждый инстанс получает копию <see cref="DeliverCallEvent"/>
/// и доставляет её своим локальным подпискам (<see cref="CallEventSubscriptionsManager"/>).
/// Фильтрация по устройству/пользователю выполняется локальным менеджером; инстансы без нужного
/// подписчика — no-op.
/// </summary>
public class CallEventDeliveryConsumer(CallEventSubscriptionsManager subscriptions)
    : IConsumer<DeliverCallEvent>
{
    public Task Consume(ConsumeContext<DeliverCallEvent> context)
    {
        var msg = context.Message;
        var evt = CallEvent.Parser.ParseFrom(msg.Payload);

        return msg.Kind switch
        {
            CallEventDeliveryKind.ToUser => subscriptions.SendToUserAsync(msg.UserId, evt),
            CallEventDeliveryKind.ToUserExceptDevice =>
                subscriptions.SendToUserExceptDeviceAsync(msg.UserId, msg.ExceptDeviceId, evt),
            CallEventDeliveryKind.ToUsers => subscriptions.SendToUsersAsync(msg.UserIds, evt),
            _ => Task.CompletedTask,
        };
    }
}
