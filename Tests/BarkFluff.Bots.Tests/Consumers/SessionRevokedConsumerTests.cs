using BarkFluff.Bots.Consumers;
using BarkFluff.GrpcServer.Metrics;
using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Shared.Queue.Identity;

using MassTransit;

using Microsoft.Extensions.Logging;

using Moq;

using Xunit;

namespace BarkFluff.Bots.Tests.Consumers;

public class SessionRevokedConsumerTests
{
    [Fact]
    public async Task Consume_RevokesSession()
    {
        var cache = new TokenRevocationCache();
        var consumer = new SessionRevokedConsumer(cache, new MetricsCollector(), Mock.Of<ILogger<SessionRevokedConsumer>>());

        await consumer.Consume(CreateContext(new SessionRevokedEvent
        {
            UserId = 123,
            DeviceId = "dev-456",
            AccessTokenExpiresAt = DateTime.UtcNow.AddHours(1)
        }).Object);

        Assert.True(cache.IsRevoked(123, "dev-456"));
    }

    [Fact]
    public async Task Consume_DoesNotRevokeOtherSessions()
    {
        var cache = new TokenRevocationCache();
        var consumer = new SessionRevokedConsumer(cache, new MetricsCollector(), Mock.Of<ILogger<SessionRevokedConsumer>>());

        await consumer.Consume(CreateContext(new SessionRevokedEvent
        {
            UserId = 123,
            DeviceId = "dev-456",
            AccessTokenExpiresAt = DateTime.UtcNow.AddHours(1)
        }).Object);

        Assert.False(cache.IsRevoked(999, "dev-456"));
        Assert.False(cache.IsRevoked(123, "other-device"));
    }

    private static Mock<ConsumeContext<SessionRevokedEvent>> CreateContext(SessionRevokedEvent message)
    {
        var context = new Mock<ConsumeContext<SessionRevokedEvent>>();
        context.Setup(c => c.Message).Returns(message);
        return context;
    }
}
