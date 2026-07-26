using BarkFluff.Federation.Services;

namespace BarkFluff.Federation.Tests.Infrastructure;

/// <summary>
/// Лимитер FetchFile без Redis (этап 3.2). По умолчанию пропускает всё; <see cref="Limit"/>
/// задаёт число разрешённых запросов на origin, чтобы проверить отказ.
/// </summary>
public sealed class FakeFetchFileRateLimiter : IFetchFileRateLimiter
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
