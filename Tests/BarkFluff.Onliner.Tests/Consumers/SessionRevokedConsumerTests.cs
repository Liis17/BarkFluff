using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Onliner.Consumers;
using BarkFluff.Shared.Queue.Identity;
using MassTransit;

namespace BarkFluff.Onliner.Tests.Consumers;

public class SessionRevokedConsumerTests
{
    private readonly TokenRevocationCache _cache = new();
    private readonly MetricsCollector _metrics = new();

    private SessionRevokedConsumer CreateConsumer()
    {
        return new SessionRevokedConsumer(
            _cache,
            _metrics,
            TestHelper.CreateLogger<SessionRevokedConsumer>());
    }

    private ConsumeContext<SessionRevokedEvent> CreateConsumeContext(
        long userId, string deviceId, DateTime expiresAt)
    {
        var msg = new SessionRevokedEvent
        {
            UserId = userId,
            DeviceId = deviceId,
            AccessTokenExpiresAt = expiresAt
        };

        var mock = new Mock<ConsumeContext<SessionRevokedEvent>>();
        mock.SetupGet(c => c.Message).Returns(msg);
        return mock.Object;
    }

    [Fact]
    public async Task Consume_AddsTokenToCache()
    {
        var consumer = CreateConsumer();
        var expiresAt = DateTime.UtcNow.AddHours(1);
        var context = CreateConsumeContext(1, "device-1", expiresAt);

        await consumer.Consume(context);

        _cache.IsRevoked(1, "device-1").Should().BeTrue();
    }

    [Fact]
    public async Task Consume_IncrementsSessionsRevokedMetric()
    {
        var consumer = CreateConsumer();
        var context = CreateConsumeContext(1, "device-1", DateTime.UtcNow.AddHours(1));

        await consumer.Consume(context);

        var snapshot = _metrics.SnapshotAndReset();
        snapshot.Should().ContainKey("sessions_revoked");
    }

    [Fact]
    public async Task Consume_DifferentDevices_AddedSeparately()
    {
        var consumer = CreateConsumer();
        var ctx1 = CreateConsumeContext(1, "device-1", DateTime.UtcNow.AddHours(1));
        var ctx2 = CreateConsumeContext(1, "device-2", DateTime.UtcNow.AddHours(1));

        await consumer.Consume(ctx1);
        await consumer.Consume(ctx2);

        _cache.IsRevoked(1, "device-1").Should().BeTrue();
        _cache.IsRevoked(1, "device-2").Should().BeTrue();
    }

    [Fact]
    public async Task Consume_DifferentUsers_AddedSeparately()
    {
        var consumer = CreateConsumer();
        var ctx1 = CreateConsumeContext(1, "device-1", DateTime.UtcNow.AddHours(1));
        var ctx2 = CreateConsumeContext(2, "device-1", DateTime.UtcNow.AddHours(1));

        await consumer.Consume(ctx1);
        await consumer.Consume(ctx2);

        _cache.IsRevoked(1, "device-1").Should().BeTrue();
        _cache.IsRevoked(2, "device-1").Should().BeTrue();
    }

    [Fact]
    public async Task Consume_DoesNotAffectOtherDevices()
    {
        var consumer = CreateConsumer();
        var context = CreateConsumeContext(1, "device-1", DateTime.UtcNow.AddHours(1));

        await consumer.Consume(context);

        _cache.IsRevoked(1, "device-2").Should().BeFalse();
    }

    [Fact]
    public async Task Consume_MultipleRevocationsForSameDevice_Overwrites()
    {
        var consumer = CreateConsumer();
        var ctx1 = CreateConsumeContext(1, "device-1", DateTime.UtcNow.AddHours(1));
        var ctx2 = CreateConsumeContext(1, "device-1", DateTime.UtcNow.AddHours(2));

        await consumer.Consume(ctx1);
        await consumer.Consume(ctx2);

        _cache.IsRevoked(1, "device-1").Should().BeTrue();
        var snapshot = _metrics.SnapshotAndReset();
        snapshot["sessions_revoked"].Should().Be(2);
    }

    [Fact]
    public async Task Consume_ReturnsCompletedTask()
    {
        var consumer = CreateConsumer();
        var context = CreateConsumeContext(1, "device-1", DateTime.UtcNow.AddHours(1));

        var result = consumer.Consume(context);

        result.IsCompleted.Should().BeTrue();
    }
}
