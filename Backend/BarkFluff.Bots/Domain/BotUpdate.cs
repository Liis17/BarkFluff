namespace BarkFluff.Bots.Domain;

public class BotUpdate
{
    /// <summary>Идентификатор (= update_id, монотонно растущий IDENTITY)</summary>
    public long Id { get; set; }

    public long BotId { get; set; }

    public Bot? Bot { get; set; }

    /// <summary>Готовый Telegram-like update без update_id (jsonb)</summary>
    public string Payload { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
}
