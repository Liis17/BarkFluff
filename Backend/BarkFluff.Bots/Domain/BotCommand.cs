using System.Text.Json.Serialization;

namespace BarkFluff.Bots.Domain;

/// <summary>Команда бота (setMyCommands). Хранится в Bots.Commands как jsonb-массив.</summary>
public class BotCommand
{
    /// <summary>Имя без слэша: a-z, 0-9, _ (1–32 символа)</summary>
    [JsonPropertyName("command")]
    public string Command { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;
}
