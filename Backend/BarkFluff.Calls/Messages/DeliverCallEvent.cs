namespace BarkFluff.Calls.Messages;

/// <summary>
/// Внутреннее fan-out сообщение доставки события звонка. Публикуется на каждое событие
/// ринга/ответа/завершения; консьюмер на КАЖДОМ инстансе (fan-out очередь) доставляет его
/// своим локальным gRPC-подпискам через <c>CallEventSubscriptionsManager</c>. Так событие
/// звонка доходит до подписчика независимо от того, на каком инстансе живёт его стрим
/// (см. docs/scaling/calls.md). Инстансы без нужного подписчика — no-op.
/// </summary>
public class DeliverCallEvent
{
    public CallEventDeliveryKind Kind { get; set; }

    /// <summary>Получатель для <see cref="CallEventDeliveryKind.ToUser"/> / <see cref="CallEventDeliveryKind.ToUserExceptDevice"/>.</summary>
    public long UserId { get; set; }

    /// <summary>Получатели для <see cref="CallEventDeliveryKind.ToUsers"/>.</summary>
    public List<long> UserIds { get; set; } = [];

    /// <summary>Устройство-исключение для <see cref="CallEventDeliveryKind.ToUserExceptDevice"/> (гасим ринг на остальных устройствах).</summary>
    public Guid ExceptDeviceId { get; set; }

    /// <summary>Сериализованный <c>BarkFluff.Proto.Calls.CallEvent</c> (Shared-транспорт не зависит от proto).</summary>
    public byte[] Payload { get; set; } = [];
}

public enum CallEventDeliveryKind
{
    ToUser,
    ToUsers,
    ToUserExceptDevice,
}
