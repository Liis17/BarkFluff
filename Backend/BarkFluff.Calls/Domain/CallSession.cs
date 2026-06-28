using System.ComponentModel.DataAnnotations;

namespace BarkFluff.Calls.Domain;

/// <summary>
/// Запись о звонке (CDR): жизненный цикл ringing → active → ended + история для клиентов.
/// Личный звонок: задан <see cref="CalleeUserId"/>, <see cref="ChatId"/> = null.
/// Групповой звонок: задан <see cref="ChatId"/>, <see cref="CalleeUserId"/> = null.
/// </summary>
public class CallSession
{
    [Key]
    public Guid Id { get; set; }

    public long CallerUserId { get; set; }

    /// <summary>Получатель личного звонка (null для группового).</summary>
    public long? CalleeUserId { get; set; }

    /// <summary>Чат группового звонка (null для личного).</summary>
    public Guid? ChatId { get; set; }

    /// <summary>Имя комнаты LiveKit (`call:{Id}`).</summary>
    public string RoomName { get; set; } = string.Empty;

    public CallMediaKind Media { get; set; }

    public CallStatus Status { get; set; }

    public CallEndReasonKind EndReason { get; set; }

    public DateTime StartedAt { get; set; }

    /// <summary>Когда первый участник принял звонок (null, если не приняли).</summary>
    public DateTime? AnsweredAt { get; set; }

    public DateTime? EndedAt { get; set; }

    public long? EndedByUserId { get; set; }

    public bool IsGroup => ChatId.HasValue;

    /// <summary>Длительность разговора в секундах (0 для несостоявшихся).</summary>
    public long DurationSeconds =>
        AnsweredAt is { } answered && EndedAt is { } ended
            ? Math.Max(0, (long)(ended - answered).TotalSeconds)
            : 0;
}
