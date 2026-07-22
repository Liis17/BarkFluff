namespace BarkFluff.Onliner.Messages;

/// <summary>
/// Внутреннее fan-out сообщение об изменении онлайн-статуса. Публикуется при переходе
/// online (heartbeat) и offline (детектор); консьюмер на КАЖДОМ инстансе доставляет его своим
/// локальным подписчикам. Так статус доходит до подписчика, чей стрим живёт на другом инстансе.
/// </summary>
public class OnlineStatusChangedEvent
{
    public long UserId { get; set; }

    /// <summary>Значение <c>Domain.Enums.StatusTypeId</c>.</summary>
    public int Status { get; set; }

    public DateTime LastSeen { get; set; }
}
