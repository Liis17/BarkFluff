using System.Collections.Concurrent;

namespace BarkFluff.Bots.Services;

/// <summary>
/// Per-bot rate-limit внешнего API (общий для gRPC-интерцептора и HTTP endpoint-фильтра).
/// Fixed window 1 секунда, 30 запросов на бота (как у Telegram).
/// </summary>
public class BotRateLimiter
{
    private const int RequestsPerSecond = 30;

    private readonly ConcurrentDictionary<long, (long WindowSecond, int Count)> _windows = new();

    public bool TryAcquire(long botId)
    {
        var nowSecond = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var updated = _windows.AddOrUpdate(
            botId,
            _ => (nowSecond, 1),
            (_, current) => current.WindowSecond == nowSecond
                ? (current.WindowSecond, current.Count + 1)
                : (nowSecond, 1));

        return updated.Count <= RequestsPerSecond;
    }
}
