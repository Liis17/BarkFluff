namespace BarkFluff.Bots.Domain;

public class Bot
{
    /// <summary>Идентификатор бота (= Users.Id)</summary>
    public long Id { get; set; }

    /// <summary>Владелец бота (NULL = системный)</summary>
    public long? OwnerUserId { get; set; }

    public string Username { get; set; } = string.Empty;

    /// <summary>Отображаемое имя (кэш из Users)</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Идентификатор выпуска bot-JWT (claim x-bot-token-id; plaintext-JWT не хранится)</summary>
    public string TokenId { get; set; } = string.Empty;

    public SystemBotRole SystemRole { get; set; }

    /// <summary>Последний подтверждённый update_id (getUpdates offset)</summary>
    public long LastConfirmedUpdateId { get; set; }

    /// <summary>Команды бота (setMyCommands) — jsonb-массив <see cref="BotCommand"/>; NULL = команд нет</summary>
    public string? Commands { get; set; }

    public DateTime CreatedAt { get; set; }
}
