using System.Collections.Concurrent;

using BarkFluff.Bots.Domain;
using BarkFluff.Bots.Messages;

using MassTransit;

using Microsoft.Extensions.Logging;

namespace BarkFluff.Bots.Services;

/// <summary>
/// Локальный кэш реестра ботов для быстрых проверок (XAuth, роутинг). Загружается при старте
/// (SystemBotsSeeder), обновляется в местах записи. При масштабировании изменения (Set/Remove)
/// рассылаются fan-out событием <see cref="BotRegistryChangedEvent"/>, чтобы все инстансы
/// перечитали бота из БД — иначе XAuth на другом инстансе видел бы старый TokenId после регенерации.
/// </summary>
public class BotRegistryCache
{
    private readonly ConcurrentDictionary<long, Bot> _bots = new();
    private readonly IBus _bus;
    private readonly ILogger<BotRegistryCache> _logger;

    public BotRegistryCache(IBus bus, ILogger<BotRegistryCache> logger)
    {
        _bus = bus;
        _logger = logger;
    }

    public void Load(IEnumerable<Bot> bots)
    {
        _bots.Clear();
        foreach (var bot in bots)
            _bots[bot.Id] = bot;
    }

    public void Set(Bot bot)
    {
        _bots[bot.Id] = bot;
        PublishChange(bot.Id, removed: false);
    }

    public void Remove(long botId)
    {
        _bots.TryRemove(botId, out _);
        PublishChange(botId, removed: true);
    }

    /// <summary>Локальное применение изменения из fan-out события (без повторной публикации).</summary>
    public void ApplySet(Bot bot) => _bots[bot.Id] = bot;

    /// <summary>Локальное применение удаления из fan-out события (без повторной публикации).</summary>
    public void ApplyRemove(long botId) => _bots.TryRemove(botId, out _);

    public Bot? Get(long botId) => _bots.GetValueOrDefault(botId);

    public bool IsBot(long userId) => _bots.ContainsKey(userId);

    public Bot? GetBySystemRole(SystemBotRole role)
        => _bots.Values.FirstOrDefault(b => b.SystemRole == role);

    /// <summary>Из списка userId вернуть тех, кто является ботом.</summary>
    public List<long> FilterBotIds(IEnumerable<long> userIds)
        => userIds.Where(_bots.ContainsKey).ToList();

    // Инвалидация — best-effort и вне горячего пути записи (нечастые операции: создание/регенерация/
    // удаление бота), поэтому публикуем fire-and-forget, не меняя sync-сигнатуры Set/Remove.
    private void PublishChange(long botId, bool removed)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await _bus.Publish(new BotRegistryChangedEvent { BotId = botId, Removed = removed });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Не удалось опубликовать инвалидацию реестра для бота {BotId}", botId);
            }
        });
    }
}
