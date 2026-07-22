using System.Collections.Concurrent;

using BarkFluff.Bots.Services;

namespace BarkFluff.Bots.Tests.Fakes;

/// <summary>In-memory fake <see cref="IBotPollingGuard"/> для тестов (TryAdd/TryRemove, Renew — no-op).</summary>
public sealed class InMemoryBotPollingGuard : IBotPollingGuard
{
    private readonly ConcurrentDictionary<long, byte> _active = new();

    public Task<bool> TryEnterAsync(long botId) => Task.FromResult(_active.TryAdd(botId, 0));

    public Task RenewAsync(long botId) => Task.CompletedTask;

    public Task ExitAsync(long botId)
    {
        _active.TryRemove(botId, out _);
        return Task.CompletedTask;
    }
}
