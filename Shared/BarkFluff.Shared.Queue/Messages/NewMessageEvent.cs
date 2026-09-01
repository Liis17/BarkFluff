using BarkFluff.Shared.Queue.Federation;

namespace BarkFluff.Shared.Queue.Messages;

public class NewMessageEvent
{
    /// <summary>Стабильный ID outbox-события для корреляции at-least-once доставки.</summary>
    public Guid EventId { get; set; }

    public Guid ChatId { get; set; }

    public List<long> ChatMembers { get; set; }

    public byte[] Message { get; set; }

    // Федеративный контекст (этап 2.2): поля добавлены, Messages начинает заполнять в 2.3.
    // Старые сообщения очереди десериализуются без этих полей — обратная совместимость сохраняется.

    /// <summary>
    /// true если в чате есть remote-участники и событие должно попасть в Federation outbox.
    /// </summary>
    public bool IsFederated { get; set; }

    /// <summary>
    /// Remote-участники чата (для DM — один). Federation строит одну строку outbox на каждый ServerName.
    /// </summary>
    public List<FederatedParticipant> RemoteParticipants { get; set; } = [];

    /// <summary>UUID сообщения на origin-ноде (для идемпотентности на приёмнике).</summary>
    public Guid? FederatedId { get; set; }

    /// <summary>UUID отправителя (для remote-авторов).</summary>
    public Guid? SenderUuid { get; set; }

    /// <summary>Время последнего изменения (SentAt/EditedAt) — basis для LWW на приёмнике.</summary>
    public DateTimeOffset? LastChangeAt { get; set; }

    /// <summary>
    /// true если это первое сообщение нового федеративного чата — Federation отправит два события:
    /// ChatCreated, затем NewMessage (упорядочено по Id).
    /// </summary>
    public bool IsFirstMessageInChat { get; set; }

    /// <summary>UUID инициатора чата (для ChatCreated).</summary>
    public Guid? InitiatorUuid { get; set; }

    /// <summary>UUID приглашённого (для ChatCreated).</summary>
    public Guid? InviteeUuid { get; set; }

    /// <summary>FID отправителя (@username:servername) — для ChatCreated и подписи remote-отправителя.</summary>
    // SenderDisplayName (отображаемое имя для пушей) заводится в 2.8 — вместе с потреблением.
    public string? SenderFid { get; set; }

    /// <summary>
    /// Снапшот метаданных вложений (этап 3.1). Заполняется только для fed-чатов; null/пусто —
    /// вложений нет. Байты не реплицируются, только метаданные.
    /// </summary>
    public List<FederatedFileRefInfo>? FederatedAttachments { get; set; }

    /// <summary>
    /// FederatedId сообщения, на которое отвечает это. Локальный ReplyToMessageId через границу
    /// ноды бессмысленен — у копии на другой ноде свой Messages.Id. Null = не ответ, либо оригинал
    /// не федеративный (ответ на локальное сообщение в fed-чате не воспроизводим у партнёра).
    /// </summary>
    public Guid? ReplyToFederatedMessageId { get; set; }
}
