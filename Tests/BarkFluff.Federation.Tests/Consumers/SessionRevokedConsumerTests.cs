using BarkFluff.Federation.Consumers;
using BarkFluff.GrpcServer.Metrics;
using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Shared.Queue.Identity;

using MassTransit;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

namespace BarkFluff.Federation.Tests.Consumers;

public class SessionRevokedConsumerTests
{
    private static ConsumeContext<SessionRevokedEvent> ConsumeContextOf(SessionRevokedEvent message)
    {
        var context = new Mock<ConsumeContext<SessionRevokedEvent>>();
        context.Setup(c => c.Message).Returns(message);
        return context.Object;
    }

    [Fact]
    public async Task Consume_RevokesSessionInCache()
    {
        var cache = new TokenRevocationCache();
        var consumer = new SessionRevokedConsumer(cache, new MetricsCollector(), NullLogger<SessionRevokedConsumer>.Instance);

        await consumer.Consume(ConsumeContextOf(new SessionRevokedEvent
        {
            UserId = 42,
            DeviceId = "device-1",
            AccessTokenExpiresAt = DateTime.UtcNow.AddHours(1),
        }));

        cache.IsRevoked(42, "device-1").Should().BeTrue();
        cache.IsRevoked(42, "device-2").Should().BeFalse();
        cache.IsRevoked(43, "device-1").Should().BeFalse();
    }
}
