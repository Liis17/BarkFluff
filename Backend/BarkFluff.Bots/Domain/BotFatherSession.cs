namespace BarkFluff.Bots.Domain;

/// <summary>
/// Состояние диалога пользователя с BotFather (state machine, TTL 30 мин по UpdatedAt).
/// </summary>
public class BotFatherSession
{
    /// <summary>Идентификатор пользователя (PK)</summary>
    public long UserId { get; set; }

    public int State { get; set; }

    /// <summary>Бот, над которым выполняется текущая операция (/setname и т.п.)</summary>
    public long? ContextBotId { get; set; }

    /// <summary>Имя, введённое на шаге /newbot (до ввода username)</summary>
    public string? PendingName { get; set; }

    public DateTime UpdatedAt { get; set; }
}
