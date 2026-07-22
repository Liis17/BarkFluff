using System.Collections.Concurrent;

using BarkFluff.Bots.Services;

namespace BarkFluff.Bots.Tests.Fakes;

/// <summary>In-memory fake <see cref="IBotRateLimiter"/> для тестов (та же логика fixed window 30/s, что у Redis-версии).</summary>
public sealed class InMemoryBotRateLimiter : IBotRateLimiter
{
    private const int RequestsPerSecond = 30;

    private readonly ConcurrentDictionary<long, (long WindowSecond, int Count)> _windows = new();

    public Task<bool> TryAcquireAsync(long botId)
    {
        var nowSecond = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var updated = _windows.AddOrUpdate(
            botId,
            _ => (nowSecond, 1),
            (_, current) => current.WindowSecond == nowSecond
                ? (current.WindowSecond, current.Count + 1)
                : (nowSecond, 1));

        return Task.FromResult(updated.Count <= RequestsPerSecond);
    }
}
