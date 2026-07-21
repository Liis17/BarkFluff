using BarkFluff.Federation.Services;

using Microsoft.Extensions.Configuration;

using Moq;

using StackExchange.Redis;

namespace BarkFluff.Federation.Tests.Services;

public class ChatCreatedQuotaLimiterTests
{
    private readonly Mock<IConnectionMultiplexer> _redis = new();
    private readonly Mock<IDatabase> _db = new();
    private readonly Mock<IConfiguration> _configuration = new();

    public ChatCreatedQuotaLimiterTests()
    {
        _redis.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(_db.Object);
    }

    private ChatCreatedQuotaLimiter CreateLimiter() => new(_redis.Object, _configuration.Object);

    [Fact]
    public async Task TryConsumeAsync_SameEventIdRetried_OnlyIncrementsOnce()
    {
        // Баг #14: ретрай ещё не обработанного ChatCreated (OutboxDispatcher.ApplyRetry) не должен
        // списывать квоту origin повторно — иначе временная недоступность Messages сама по себе
        // исчерпывает лимит.
        var eventId = Guid.NewGuid();
        var firstCall = true;
        _db.Setup(d => d.StringSetAsync(
                It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(), It.IsAny<When>()))
            .ReturnsAsync(() =>
            {
                var result = firstCall;
                firstCall = false;
                return result;
            });
        _db.Setup(d => d.StringIncrementAsync(It.IsAny<RedisKey>(), It.IsAny<long>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(1);
        var limiter = CreateLimiter();

        var first = await limiter.TryConsumeAsync("peer.test", eventId);
        var second = await limiter.TryConsumeAsync("peer.test", eventId);

        first.Should().BeTrue();
        second.Should().BeTrue();
        _db.Verify(d => d.StringIncrementAsync(It.IsAny<RedisKey>(), It.IsAny<long>(), It.IsAny<CommandFlags>()), Times.Once);
    }

    [Fact]
    public async Task TryConsumeAsync_DifferentEventIds_EachIncrementsOnce()
    {
        _db.Setup(d => d.StringSetAsync(
                It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(), It.IsAny<When>()))
            .ReturnsAsync(true);
        _db.Setup(d => d.StringIncrementAsync(It.IsAny<RedisKey>(), It.IsAny<long>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(1);
        var limiter = CreateLimiter();

        await limiter.TryConsumeAsync("peer.test", Guid.NewGuid());
        await limiter.TryConsumeAsync("peer.test", Guid.NewGuid());

        _db.Verify(d => d.StringIncrementAsync(It.IsAny<RedisKey>(), It.IsAny<long>(), It.IsAny<CommandFlags>()), Times.Exactly(2));
    }
}
