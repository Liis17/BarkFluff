using BarkFluff.Files.Consumers;
using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Shared.Queue.Identity;

namespace BarkFluff.Files.Tests.Consumers;

public class SessionRevokedConsumerTests
{
    [Fact]
    public async Task Consume_RevokeSession_AddsToCache()
    {
        var cache = new TokenRevocationCache();
        var consumer = new SessionRevokedConsumer(cache, Mock.Of<ILogger<SessionRevokedConsumer>>());

        var expiresAt = DateTime.UtcNow.AddHours(1);
        var context = new Mock<ConsumeContext<SessionRevokedEvent>>();
        context.Setup(c => c.Message).Returns(new SessionRevokedEvent
        {
            UserId = 1,
            DeviceId = "device-1",
            AccessTokenExpiresAt = expiresAt
        });

        await consumer.Consume(context.Object);

        cache.IsRevoked(1, "device-1").Should().BeTrue();
    }

    [Fact]
    public async Task Consume_DifferentDevices_BothRevoked()
    {
        var cache = new TokenRevocationCache();
        var consumer = new SessionRevokedConsumer(cache, Mock.Of<ILogger<SessionRevokedConsumer>>());
        var expiresAt = DateTime.UtcNow.AddHours(1);

        var ctx1 = new Mock<ConsumeContext<SessionRevokedEvent>>();
        ctx1.Setup(c => c.Message).Returns(new SessionRevokedEvent
        {
            UserId = 1, DeviceId = "d1", AccessTokenExpiresAt = expiresAt
        });

        var ctx2 = new Mock<ConsumeContext<SessionRevokedEvent>>();
        ctx2.Setup(c => c.Message).Returns(new SessionRevokedEvent
        {
            UserId = 1, DeviceId = "d2", AccessTokenExpiresAt = expiresAt
        });

        await consumer.Consume(ctx1.Object);
        await consumer.Consume(ctx2.Object);

        cache.IsRevoked(1, "d1").Should().BeTrue();
        cache.IsRevoked(1, "d2").Should().BeTrue();
    }

    [Fact]
    public async Task Consume_UserNotRevoked_ReturnsFalse()
    {
        var cache = new TokenRevocationCache();
        var consumer = new SessionRevokedConsumer(cache, Mock.Of<ILogger<SessionRevokedConsumer>>());

        var context = new Mock<ConsumeContext<SessionRevokedEvent>>();
        context.Setup(c => c.Message).Returns(new SessionRevokedEvent
        {
            UserId = 1, DeviceId = "d1", AccessTokenExpiresAt = DateTime.UtcNow.AddHours(1)
        });

        await consumer.Consume(context.Object);

        cache.IsRevoked(2, "d1").Should().BeFalse();
    }
}
