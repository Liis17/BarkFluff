namespace BarkFluff.Shared.Queue.Messages;

/// <summary>
/// Чат скрыт (DeleteChat) у одного или обоих участников. Updates рассылает
/// каждому userId из ChatMembers на всех его устройствах (user-scope).
/// </summary>
public class ChatHiddenEvent
{
    public Guid ChatId { get; set; }

    public List<long> ChatMembers { get; set; } = [];

    /// <summary>
    /// true, если вместе со скрытием чата был стёрт контент всех его сообщений
    /// (delete_for_everyone) — клиент должен очистить локальный кеш сообщений чата.
    /// </summary>
    public bool ContentWiped { get; set; }
}
