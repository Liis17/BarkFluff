using BarkFluff.Shared.Queue.Federation;

namespace BarkFluff.Shared.Queue.Messages;

public class MessageDeletedEvent
{
    public Guid ChatId { get; set; }

    public List<long> ChatMembers { get; set; }

    public long MessageId { get; set; }

    // Федеративный контекст (этап 2.2). Заполняет Messages с 2.3.
    // MessageId здесь — локальный long; Federation использует FederatedId для идентификации
    // сообщения на origin-ноде (импорт на приёмнике ищет по FederatedId).
    public bool IsFederated { get; set; }

    public List<FederatedParticipant> RemoteParticipants { get; set; } = [];

    public Guid? FederatedId { get; set; }

    public DateTimeOffset? LastChangeAt { get; set; }
}
