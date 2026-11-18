namespace BarkFluff.Shared.Queue.Users
{
    /// <summary>
    /// Папки чатов пользователя изменились (создание, правка, удаление, состав, порядок).
    /// Сигнал без снимка: подписчики перечитывают папки через Users, поэтому порядок
    /// и повторная доставка событий не влияют на итоговое состояние.
    /// </summary>
    public class ChatFoldersChanged
    {
        public long UserId { get; set; }
    }
}
