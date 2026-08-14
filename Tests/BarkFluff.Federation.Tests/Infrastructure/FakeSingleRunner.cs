using BarkFluff.Federation.Services;

namespace BarkFluff.Federation.Tests.Infrastructure;

/// <summary>
/// Single-runner без Redis: по умолчанию всегда лидер; <see cref="Leader"/> = false эмулирует
/// захват лидерства другим инстансом (тики пропускаются).
/// </summary>
public sealed class FakeSingleRunner : ISingleRunner
{
    public bool Leader { get; set; } = true;

    public Task<bool> TryAcquireAsync(string lockKey, TimeSpan ttl)
        => Task.FromResult(Leader);
}
