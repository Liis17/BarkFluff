using BarkFluff.GrpcServer.Metrics;
using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Identity.Consumers;
using BarkFluff.Shared.Queue.Identity;

using MassTransit;

using Microsoft.Extensions.Logging;

using Moq;

using Xunit;

namespace BarkFluff.Identity.Tests.Consumers;

public class SessionRevokedConsumerTests
{
    private readonly TokenRevocationCache _cache;
    private readonly MetricsCollector _metrics;
    private readonly Mock<ILogger<SessionRevokedConsumer>> _logger;

    public SessionRevokedConsumerTests()
    {
        _cache = new TokenRevocationCache();
        _metrics = new MetricsCollector();
        _logger = new Mock<ILogger<SessionRevokedConsumer>>();
    }

    [Fact]
    public async Task Consume_RevokesSessionInCache()
    {
        var consumer = new SessionRevokedConsumer(_cache, _metrics, _logger.Object);
        var message = new SessionRevokedEvent
        {
            UserId = 42,
            DeviceId = "dev1",
            AccessTokenExpiresAt = DateTime.UtcNow.AddMinutes(30)
        };

        var context = new Mock<ConsumeContext<SessionRevokedEvent>>();
        context.SetupGet(c => c.Message).Returns(message);

        await consumer.Consume(context.Object);

        Assert.True(_cache.IsRevoked(42, "dev1"));
        var snapshot = _metrics.SnapshotAndReset();
        Assert.Equal(1, snapshot.GetValueOrDefault("rabbitmq_events_consumed"));
        Assert.Equal(1, snapshot.GetValueOrDefault("session_revocations_received"));
    }
}
