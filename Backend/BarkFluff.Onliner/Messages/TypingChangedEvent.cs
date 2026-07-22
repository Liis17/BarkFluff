namespace BarkFluff.Onliner.Messages;

/// <summary>
/// Внутреннее fan-out сообщение индикатора набора текста. Публикуется на SetTypingStatus;
/// консьюмер на КАЖДОМ инстансе ретранслирует его своим локальным подписчикам чата
/// (кроме самого печатающего — фильтрация в локальном менеджере). Эфемерно, в Redis не хранится.
/// </summary>
public class TypingChangedEvent
{
    public string ChatId { get; set; } = string.Empty;

    public long UserId { get; set; }

    /// <summary>Значение proto <c>TypingAction</c>.</summary>
    public int Action { get; set; }
}
