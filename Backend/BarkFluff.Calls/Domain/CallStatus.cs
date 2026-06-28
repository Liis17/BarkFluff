namespace BarkFluff.Calls.Domain;

public enum CallStatus
{
    /// <summary>Создан, идёт ринг — ждём ответа.</summary>
    Ringing = 0,

    /// <summary>Принят, медиа идёт через LiveKit.</summary>
    Active = 1,

    /// <summary>Завершён (положили трубку / отклонён / пропущен / сбой).</summary>
    Ended = 2,
}
