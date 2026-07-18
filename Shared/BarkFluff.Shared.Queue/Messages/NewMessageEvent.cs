using BarkFluff.Shared.Queue.Federation;

namespace BarkFluff.Shared.Queue.Messages;

public class NewMessageEvent
{
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

    // Для пушей (этап 2.8) — завести поля сразу.
    public string? SenderDisplayName { get; set; }
    public string? SenderFid { get; set; }
}
