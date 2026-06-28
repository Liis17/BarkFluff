using BarkFluff.Users.Infrastructure;
using FluentAssertions;
using MassTransit;
using Moq;

namespace BarkFluff.Users.Tests.Infrastructure;

public class UserInfoQueueSenderTests
{
    private readonly Mock<IPublishEndpoint> _publishEndpoint;
    private readonly GrpcServer.Metrics.MetricsCollector _metrics;
    private readonly UserInfoQueueSender _sender;

    public UserInfoQueueSenderTests()
    {
        _publishEndpoint = new Mock<IPublishEndpoint>();
        _metrics = new GrpcServer.Metrics.MetricsCollector();
        _sender = new UserInfoQueueSender(_publishEndpoint.Object, _metrics);
    }

    [Fact]
    public async Task NameChangedEvent_PublishesCorrectEvent()
    {
        await _sender.NameChangedEvent(123, "John", "Doe");

        _publishEndpoint.Verify(
            p => p.Publish(It.Is<BarkFluff.Shared.Queue.Users.UserChangedName>(
                e => e.UserId == 123 && e.NewFirstName == "John" && e.NewLastName == "Doe"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task UsernameChangedEvent_PublishesCorrectEvent()
    {
        await _sender.UsernameChangedEvent(123, "newname");

        _publishEndpoint.Verify(
            p => p.Publish(It.Is<BarkFluff.Shared.Queue.Users.UserChangedUsername>(
                e => e.UserId == 123 && e.NewUsername == "newname"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task UserChangedAvatarEvent_PublishesCorrectEvent()
    {
        await _sender.UserChangedAvatarEvent(123, "url.png", "preview.png");

        _publishEndpoint.Verify(
            p => p.Publish(It.Is<BarkFluff.Shared.Queue.Users.UserChangedAvatar>(
                e => e.UserId == 123 && e.ProfilePictureUrl == "url.png" && e.ProfilePictureUrlPreview == "preview.png"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task UserChangedPasswordEvent_PublishesCorrectEvent()
    {
        await _sender.UserChangedPasswordEvent(123);

        _publishEndpoint.Verify(
            p => p.Publish(It.Is<BarkFluff.Shared.Queue.Users.UserChangedPassword>(
                e => e.UserId == 123),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task UserBioChangedEvent_PublishesCorrectEvent()
    {
        await _sender.UserBioChangedEvent(123, "New bio text");

        _publishEndpoint.Verify(
            p => p.Publish(It.Is<BarkFluff.Shared.Queue.Users.UserChangedBio>(
                e => e.UserId == 123 && e.NewBio == "New bio text"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
