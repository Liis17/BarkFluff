using BarkFluff.GrpcServer.Metrics;
using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Shared.Queue.Identity;
using BarkFluff.Users.Consumers;
using MassTransit;

namespace BarkFluff.Users.Tests.Consumers;

public class SessionRevokedConsumerTests
{
    [Fact]
    public async Task Consume_RevokesSession()
    {
        var cache = new TokenRevocationCache();
        var metrics = new MetricsCollector();
        var consumer = new SessionRevokedConsumer(cache, metrics, Tests.TestHelper.CreateLogger<SessionRevokedConsumer>());

        var context = new Mock<ConsumeContext<SessionRevokedEvent>>();
        context.Setup(c => c.Message).Returns(new SessionRevokedEvent
        {
            UserId = 123,
            DeviceId = "dev-456",
            AccessTokenExpiresAt = DateTime.UtcNow.AddHours(1)
        });

        await consumer.Consume(context.Object);

        cache.IsRevoked(123, "dev-456").Should().BeTrue();
    }

    [Fact]
    public async Task Consume_DoesNotRevokeOtherSessions()
    {
        var cache = new TokenRevocationCache();
        var metrics = new MetricsCollector();
        var consumer = new SessionRevokedConsumer(cache, metrics, Tests.TestHelper.CreateLogger<SessionRevokedConsumer>());

        var context = new Mock<ConsumeContext<SessionRevokedEvent>>();
        context.Setup(c => c.Message).Returns(new SessionRevokedEvent
        {
            UserId = 123,
            DeviceId = "dev-456",
            AccessTokenExpiresAt = DateTime.UtcNow.AddHours(1)
        });

        await consumer.Consume(context.Object);

        cache.IsRevoked(999, "dev-456").Should().BeFalse();
        cache.IsRevoked(123, "other-device").Should().BeFalse();
    }
}
