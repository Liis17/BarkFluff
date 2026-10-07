namespace BarkFluff.Shared.Queue.Messages;

/// <summary>
/// Команда CloudMessaging: удалить push-нотификацию чата на всех FCM-устройствах пользователя.
/// Публикуется из Updates после прочтения сообщения, чтобы скрыть нотификацию на остальных устройствах.
/// </summary>
public class DismissPushEvent
{
    public Guid ChatId { get; set; }

    public long UserId { get; set; }

    /// <summary>Прочитанная граница; 0 сохраняет legacy отмену всего чата.</summary>
    public long MessageId { get; set; }
}
