namespace BarkFluff.Calls.Domain;

public enum CallEndReasonKind
{
    /// <summary>Звонок ещё не завершён.</summary>
    None = 0,

    /// <summary>Завершён участником (положили трубку).</summary>
    Hangup = 1,

    /// <summary>Отклонён получателем.</summary>
    Rejected = 2,

    /// <summary>Никто не ответил (таймаут ринга).</summary>
    Missed = 3,

    /// <summary>Получатель занят другим звонком.</summary>
    Busy = 4,

    /// <summary>Сетевой или медиа-сбой.</summary>
    Failed = 5,
}
