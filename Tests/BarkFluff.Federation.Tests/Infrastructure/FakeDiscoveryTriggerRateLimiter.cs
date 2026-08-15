using BarkFluff.Federation.Services;

namespace BarkFluff.Federation.Tests.Infrastructure;

/// <summary>
/// Discovery-лимитер без Redis. По умолчанию всегда пропускает (в тестах cooldown не важен);
/// <see cref="Allowed"/> = false эмулирует активный cooldown.
/// </summary>
public sealed class FakeDiscoveryTriggerRateLimiter : IDiscoveryTriggerRateLimiter
{
    public bool Allowed { get; set; } = true;

    public Task<bool> TryTriggerAsync(string serverName)
        => Task.FromResult(Allowed);
}
