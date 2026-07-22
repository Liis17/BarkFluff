namespace BarkFluff.Bots.Messages;

/// <summary>
/// Fan-out инвалидация локального кэша реестра ботов. Публикуется при изменении/удалении бота;
/// консьюмер на КАЖДОМ инстансе перечитывает бота из БД (или удаляет из кэша). Так регенерация
/// токена / удаление бота видны всем инстансам (иначе XAuth на другом инстансе видел бы старый
/// TokenId). Несёт только BotId — актуальные данные берутся из БД.
/// </summary>
public class BotRegistryChangedEvent
{
    public long BotId { get; set; }

    public bool Removed { get; set; }
}
