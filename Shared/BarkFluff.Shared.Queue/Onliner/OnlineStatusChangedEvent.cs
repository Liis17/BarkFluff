// ВНИМАНИЕ: namespace намеренно НЕ совпадает с проектом.
//
// MassTransit выводит URN типа сообщения из namespace + имени класса. Контракт переехал сюда из
// BarkFluff.Onliner, чтобы его мог потреблять ещё и Federation (presence-мост, этап 4.3), но смена
// namespace сменила бы URN — и во время выкатки инстансы разных версий перестали бы видеть события
// друг друга. Поэтому namespace сохранён исходный.
namespace BarkFluff.Onliner.Messages;

/// <summary>
/// Внутреннее fan-out сообщение об изменении онлайн-статуса. Публикуется при переходе
/// online (heartbeat) и offline (детектор); консьюмер на КАЖДОМ инстансе доставляет его своим
/// локальным подписчикам. Так статус доходит до подписчика, чей стрим живёт на другом инстансе.
///
/// С этапа 4.3 те же события слушает Federation (своя per-instance очередь): по ним он
/// отправляет изменения в S2S-стримы нод-партнёров.
/// </summary>
public class OnlineStatusChangedEvent
{
    public long UserId { get; set; }

    /// <summary>Значение <c>Domain.Enums.StatusTypeId</c>.</summary>
    public int Status { get; set; }

    public DateTime LastSeen { get; set; }

    /// <summary>
    /// Заполнен только для remote-пользователя (этап 4.2): у него нет локального
    /// <see cref="UserId"/>, адресация подписчикам идёт по UUID. Для локальных — null,
    /// и весь прежний путь не меняется. Federation такие события игнорирует: чужие статусы
    /// пересылать обратно в федерацию нельзя.
    /// </summary>
    public Guid? UserUuid { get; set; }
}
