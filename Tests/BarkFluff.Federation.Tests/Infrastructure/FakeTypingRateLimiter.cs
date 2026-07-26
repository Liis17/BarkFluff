using BarkFluff.Federation.Services;

namespace BarkFluff.Federation.Tests.Infrastructure;

/// <summary>
/// Лимитер typing без Redis (этап 4.4). По умолчанию пропускает всё; <see cref="Limit"/>
/// задаёт число разрешённых вызовов на origin, чтобы проверить отказ.
/// </summary>
public sealed class FakeTypingRateLimiter : ITypingRateLimiter
{
    private readonly Dictionary<string, int> _consumed = [];

    /// <summary>null — лимита нет.</summary>
    public int? Limit { get; init; }

    public Task<bool> TryConsumeAsync(string origin)
    {
        _consumed.TryGetValue(origin, out var count);
        _consumed[origin] = count + 1;

        return Task.FromResult(Limit is null || count < Limit);
    }
}
