using BarkFluff.Bft;
using Xunit;

namespace BarkFluff.Bft.Tests;

public sealed class BftSaltReplayCacheTests
{
    [Fact]
    public async Task ConcurrentDuplicateAcceptsExactlyOneAndExpiresAtTtl()
    {
        var clock = new AdjustableTimeProvider(DateTimeOffset.FromUnixTimeSeconds(1_800_000_000));
        var cache = new BftSaltReplayCache(capacity: 2, timeProvider: clock);
        var salt = Enumerable.Range(0, 32).Select(value => (byte)value).ToArray();
        var results = await Task.WhenAll(Enumerable.Range(0, 128).Select(_ => Task.Run(() => cache.TryAdd(salt))));
        Assert.Equal(1, results.Count(result => result == BftSaltReplayAddResult.Added));
        Assert.Equal(127, results.Count(result => result == BftSaltReplayAddResult.Duplicate));

        var secondSalt = Enumerable.Range(32, 32).Select(value => (byte)value).ToArray();
        var thirdSalt = Enumerable.Range(64, 32).Select(value => (byte)value).ToArray();
        Assert.Equal(BftSaltReplayAddResult.Added, cache.TryAdd(secondSalt));
        Assert.Equal(BftSaltReplayAddResult.Full, cache.TryAdd(thirdSalt));
        Assert.Equal(BftSaltReplayAddResult.Duplicate, cache.TryAdd(salt));

        clock.Advance(TimeSpan.FromSeconds(300));
        Assert.Equal(BftSaltReplayAddResult.Added, cache.TryAdd(thirdSalt));
        Assert.Equal(1, cache.Count);
        Assert.Equal(BftSaltReplayAddResult.Added, cache.TryAdd(salt));
    }
}

internal sealed class AdjustableTimeProvider(DateTimeOffset now) : TimeProvider
{
    private DateTimeOffset _now = now;
    public override DateTimeOffset GetUtcNow() => _now;
    public void Advance(TimeSpan value) => _now += value;
}
