using BarkFluff.Federation.Services;

using Moq;

using StackExchange.Redis;

namespace BarkFluff.Federation.Tests.Services;

public class DiscoveryTriggerRateLimiterTests
{
    private readonly Mock<IConnectionMultiplexer> _redis = new();
    private readonly Mock<IDatabase> _db = new();

    public DiscoveryTriggerRateLimiterTests()
    {
        _redis.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(_db.Object);
    }

    private RedisDiscoveryTriggerRateLimiter CreateLimiter() => new(_redis.Object);

    [Fact]
    public async Task TryTriggerAsync_FirstCallForServer_ReturnsTrue()
    {
        // Ключа ещё нет — SET NX успешен, discovery разрешён.
        _db.Setup(d => d.StringSetAsync(
                It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(), It.IsAny<When>()))
            .ReturnsAsync(true);

        (await CreateLimiter().TryTriggerAsync("peer.test")).Should().BeTrue();
    }

    [Fact]
    public async Task TryTriggerAsync_RepeatWithinCooldown_ReturnsFalse()
    {
        // Ключ существует (cooldown активен на любом инстансе) — SET NX вернул false.
        _db.Setup(d => d.StringSetAsync(
                It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(), It.IsAny<When>()))
            .ReturnsAsync(false);

        (await CreateLimiter().TryTriggerAsync("peer.test")).Should().BeFalse();
    }

    [Fact]
    public async Task TryTriggerAsync_DifferentServers_UseDifferentKeys()
    {
        _db.Setup(d => d.StringSetAsync(
                It.Is<RedisKey>(k => k.ToString() == "fed:discovery:peer-a.test"),
                It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(), It.IsAny<When>()))
            .ReturnsAsync(true);
        _db.Setup(d => d.StringSetAsync(
                It.Is<RedisKey>(k => k.ToString() == "fed:discovery:peer-b.test"),
                It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(), It.IsAny<When>()))
            .ReturnsAsync(true);

        var limiter = CreateLimiter();

        (await limiter.TryTriggerAsync("peer-a.test")).Should().BeTrue();
        (await limiter.TryTriggerAsync("peer-b.test")).Should().BeTrue();
    }

    [Fact]
    public async Task TryTriggerAsync_UsesNxWithCooldownTtl()
    {
        TimeSpan? capturedTtl = null;
        When capturedWhen = default;
        _db.Setup(d => d.StringSetAsync(
                It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(), It.IsAny<When>()))
            .Callback<RedisKey, RedisValue, TimeSpan?, When>((_, _, ttl, when) =>
            {
                capturedTtl = ttl;
                capturedWhen = when;
            })
            .ReturnsAsync(true);

        await CreateLimiter().TryTriggerAsync("peer.test");

        capturedTtl.Should().Be(TimeSpan.FromMinutes(5), "cooldown из плана — SET NX EX 300");
        capturedWhen.Should().Be(When.NotExists);
    }
}
