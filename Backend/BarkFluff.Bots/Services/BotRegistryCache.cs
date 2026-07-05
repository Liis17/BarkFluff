using System.Collections.Concurrent;

using BarkFluff.Bots.Domain;

namespace BarkFluff.Bots.Services;

/// <summary>
/// In-memory реестр ботов. Bots — единственный писатель по своим данным,
/// поэтому кэш обновляется синхронно с БД (Redis не нужен в v1).
/// Загружается при старте (SystemBotsSeeder), обновляется в местах записи.
/// </summary>
public class BotRegistryCache
{
    private readonly ConcurrentDictionary<long, Bot> _bots = new();

    public void Load(IEnumerable<Bot> bots)
    {
        _bots.Clear();
        foreach (var bot in bots)
            _bots[bot.Id] = bot;
    }

    public void Set(Bot bot) => _bots[bot.Id] = bot;

    public void Remove(long botId) => _bots.TryRemove(botId, out _);

    public Bot? Get(long botId) => _bots.GetValueOrDefault(botId);

    public bool IsBot(long userId) => _bots.ContainsKey(userId);

    public Bot? GetBySystemRole(SystemBotRole role)
        => _bots.Values.FirstOrDefault(b => b.SystemRole == role);

    /// <summary>Из списка userId вернуть тех, кто является ботом.</summary>
    public List<long> FilterBotIds(IEnumerable<long> userIds)
        => userIds.Where(_bots.ContainsKey).ToList();
}
