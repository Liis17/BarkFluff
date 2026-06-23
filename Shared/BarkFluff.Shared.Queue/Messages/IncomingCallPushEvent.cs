namespace BarkFluff.Shared.Queue.Messages;

/// <summary>
/// Событие входящего звонка для CloudMessaging: отправить high-priority FCM push
/// получателям, чтобы ринг дошёл при background/killed app.
/// Публикуется из BarkFluff.Calls при инициации звонка.
/// </summary>
public class IncomingCallPushEvent
{
    public Guid CallId { get; set; }

    public long CallerUserId { get; set; }

    /// <summary>Получатели ринга (callee для личного / члены чата кроме инициатора для группового).</summary>
    public List<long> RecipientUserIds { get; set; } = [];

    /// <summary>Чат группового звонка (null для личного).</summary>
    public Guid? ChatId { get; set; }

    /// <summary>Значение proto CallMediaType.</summary>
    public int MediaType { get; set; }

    public DateTime StartedAt { get; set; }
}
