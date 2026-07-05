using System.Collections.Concurrent;

namespace BarkFluff.Bots.Services;

/// <summary>
/// Один активный поток получения update'ов на бота: concurrent getUpdates/SubscribeUpdates
/// запрещён (как в Telegram). Снимает и гонку по LastConfirmedUpdateId.
/// </summary>
public class BotPollingGuard
{
    private readonly ConcurrentDictionary<long, byte> _active = new();

    /// <summary>true = слот занят успешно; false = уже есть активный поток.</summary>
    public bool TryEnter(long botId) => _active.TryAdd(botId, 0);

    public void Exit(long botId) => _active.TryRemove(botId, out _);
}
