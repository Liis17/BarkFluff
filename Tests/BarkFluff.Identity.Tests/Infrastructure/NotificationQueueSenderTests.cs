using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Identity.Infrastructure;
using BarkFluff.Shared.Queue.Notifications;

using MassTransit;

using Moq;

using Xunit;

namespace BarkFluff.Identity.Tests.Infrastructure;

public class NotificationQueueSenderTests
{
    private readonly Mock<IPublishEndpoint> _publishEndpoint;
    private readonly NotificationQueueSender _sender;

    public NotificationQueueSenderTests()
    {
        _publishEndpoint = new Mock<IPublishEndpoint>();
        _sender = new NotificationQueueSender(_publishEndpoint.Object);
    }

    [Fact]
    public async Task SendNotification_EmailNotification_PublishesToBus()
    {
        var notification = new EmailNotification
        {
            Address = "test@test.com",
            Title = "Test"
        };

        await _sender.SendNotification(notification);

        _publishEndpoint.Verify(
            x => x.Publish(It.IsAny<EmailNotification>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SendNotification_EmailNotification_PublishesCorrectData()
    {
        EmailNotification? published = null;
        _publishEndpoint
            .Setup(x => x.Publish(It.IsAny<EmailNotification>(), It.IsAny<CancellationToken>()))
            .Callback<EmailNotification, CancellationToken>((n, _) => published = n);

        var notification = new EmailNotification
        {
            Address = "user@example.com",
            Title = "Test Title",
            OwnerId = 42,
            ServiceId = BarkFluff.Shared.Identity.ServiceId.Identity
        };

        await _sender.SendNotification(notification);

        Assert.NotNull(published);
        Assert.Equal("user@example.com", published.Address);
        Assert.Equal("Test Title", published.Title);
        Assert.Equal(42, published.OwnerId);
    }

    [Fact]
    public async Task SendNotification_NonEmailNotification_DoesNotPublish()
    {
        var notification = new TestNotification();

        await _sender.SendNotification(notification);

        _publishEndpoint.Verify(
            x => x.Publish(It.IsAny<EmailNotification>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private class TestNotification : Notification
    {
        public override TransportId TransportId => TransportId.Email;
    }
}
