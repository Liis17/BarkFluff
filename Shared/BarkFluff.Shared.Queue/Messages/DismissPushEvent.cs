namespace BarkFluff.Shared.Queue.Messages;

/// <summary>
/// Команда CloudMessaging: удалить push-нотификацию чата на всех FCM-устройствах пользователя.
/// Публикуется из Updates после прочтения сообщения, чтобы скрыть нотификацию на остальных устройствах.
/// </summary>
public class DismissPushEvent
{
    public Guid ChatId { get; set; }

    public long UserId { get; set; }
}
