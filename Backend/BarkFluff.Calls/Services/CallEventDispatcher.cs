using BarkFluff.Calls.Messages;
using BarkFluff.Proto.Calls;

using Google.Protobuf;

using MassTransit;

namespace BarkFluff.Calls.Services;

/// <summary>
/// Доставка событий звонка подписчикам. Абстрагирует транспорт: прод-реализация публикует
/// fan-out сообщение в RabbitMQ, чтобы каждый инстанс доставил событие своим локальным стримам —
/// это делает доставку корректной при нескольких инстансах (стрим клиента живёт на одном инстансе).
/// </summary>
public interface ICallEventDispatcher
{
    Task SendToUserAsync(long userId, CallEvent evt);

    Task SendToUserExceptDeviceAsync(long userId, Guid exceptDeviceId, CallEvent evt);

    Task SendToUsersAsync(IEnumerable<long> userIds, CallEvent evt);
}

/// <summary>Прод-реализация: публикует <see cref="DeliverCallEvent"/> в fan-out exchange RabbitMQ.</summary>
public class CallEventDispatcher(IPublishEndpoint publish) : ICallEventDispatcher
{
    public Task SendToUserAsync(long userId, CallEvent evt)
        => publish.Publish(new DeliverCallEvent
        {
            Kind = CallEventDeliveryKind.ToUser,
            UserId = userId,
            Payload = evt.ToByteArray(),
        });

    public Task SendToUserExceptDeviceAsync(long userId, Guid exceptDeviceId, CallEvent evt)
        => publish.Publish(new DeliverCallEvent
        {
            Kind = CallEventDeliveryKind.ToUserExceptDevice,
            UserId = userId,
            ExceptDeviceId = exceptDeviceId,
            Payload = evt.ToByteArray(),
        });

    public Task SendToUsersAsync(IEnumerable<long> userIds, CallEvent evt)
        => publish.Publish(new DeliverCallEvent
        {
            Kind = CallEventDeliveryKind.ToUsers,
            UserIds = userIds.ToList(),
            Payload = evt.ToByteArray(),
        });
}
