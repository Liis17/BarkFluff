using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Onliner.Features.ChangeUsersInSubscription;
using BarkFluff.Onliner.Services;
using BarkFluff.Proto.Users;
using Grpc.Core;

namespace BarkFluff.Onliner.Tests.Features.ChangeUsersInSubscription;

public class ChangeUsersInSubscriptionCommandHandlerTests
{
    private readonly TestHelper _h = new();

    private ChangeUsersInSubscriptionCommandHandler CreateHandler(long userId)
    {
        return new ChangeUsersInSubscriptionCommandHandler(
            _h.CreateUserContext(userId),
            _h.SubscriptionsManager,
            _h.CreateVisibilityFilter(),
            TestHelper.CreateLogger<ChangeUsersInSubscriptionCommandHandler>());
    }

    [Fact]
    public async Task Handle_NoActiveSubscriptions_ThrowsFailedPrecondition()
    {
        var handler = CreateHandler(1);
        var act = async () => await handler.Handle(
            new ChangeUsersInSubscriptionCommand { UserIds = [10] },
            CancellationToken.None);
        await act.Should().ThrowAsync<RpcException>()
            .Where(e => e.StatusCode == StatusCode.FailedPrecondition);
    }

    [Fact]
    public async Task Handle_WithActiveSubscription_UpdatesTrackedUsers()
    {
        var stream = _h.CreateMockStreamWriter();
        _h.SubscriptionsManager.RegisterSubscription(1, [10, 20], stream.Object);
        _h.SetupUserPrivacy(30, ProfileFieldVisibility.All);
        _h.SetupUserPrivacy(40, ProfileFieldVisibility.All);
        var handler = CreateHandler(1);

        var result = await handler.Handle(
            new ChangeUsersInSubscriptionCommand { UserIds = [30, 40] },
            CancellationToken.None);

        result.Should().NotBeNull();
        _h.SubscriptionsManager.GetStreamsTrackingUser(30).Should().HaveCount(1);
        _h.SubscriptionsManager.GetStreamsTrackingUser(40).Should().HaveCount(1);
        _h.SubscriptionsManager.GetStreamsTrackingUser(10).Should().BeEmpty();
        _h.SubscriptionsManager.GetStreamsTrackingUser(20).Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_FiltersHiddenUsers()
    {
        var stream = _h.CreateMockStreamWriter();
        _h.SubscriptionsManager.RegisterSubscription(1, [10], stream.Object);
        _h.SetupUserPrivacy(20, ProfileFieldVisibility.All);
        _h.SetupUserPrivacy(30, ProfileFieldVisibility.None);
        var handler = CreateHandler(1);

        await handler.Handle(
            new ChangeUsersInSubscriptionCommand { UserIds = [20, 30] },
            CancellationToken.None);

        _h.SubscriptionsManager.GetStreamsTrackingUser(20).Should().HaveCount(1);
        _h.SubscriptionsManager.GetStreamsTrackingUser(30).Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_SelfAlwaysIncluded()
    {
        var stream = _h.CreateMockStreamWriter();
        _h.SubscriptionsManager.RegisterSubscription(1, [10], stream.Object);
        _h.SetupUserPrivacy(1, ProfileFieldVisibility.None);
        var handler = CreateHandler(1);

        await handler.Handle(
            new ChangeUsersInSubscriptionCommand { UserIds = [1] },
            CancellationToken.None);

        _h.SubscriptionsManager.GetStreamsTrackingUser(1).Should().HaveCount(1);
    }

    [Fact]
    public async Task Handle_MultipleConnections_UpdatesAll()
    {
        var s1 = _h.CreateMockStreamWriter();
        var s2 = _h.CreateMockStreamWriter();
        _h.SubscriptionsManager.RegisterSubscription(1, [10], s1.Object);
        _h.SubscriptionsManager.RegisterSubscription(1, [20], s2.Object);
        _h.SetupUserPrivacy(30, ProfileFieldVisibility.All);
        var handler = CreateHandler(1);

        await handler.Handle(
            new ChangeUsersInSubscriptionCommand { UserIds = [30] },
            CancellationToken.None);

        _h.SubscriptionsManager.GetStreamsTrackingUser(30).Should().HaveCount(2);
    }

    [Fact]
    public async Task Handle_EmptyUserIds_UpdatesToEmpty()
    {
        var stream = _h.CreateMockStreamWriter();
        _h.SubscriptionsManager.RegisterSubscription(1, [10, 20], stream.Object);
        var handler = CreateHandler(1);

        await handler.Handle(
            new ChangeUsersInSubscriptionCommand { UserIds = [] },
            CancellationToken.None);

        _h.SubscriptionsManager.GetStreamsTrackingUser(10).Should().BeEmpty();
        _h.SubscriptionsManager.GetStreamsTrackingUser(20).Should().BeEmpty();
        _h.SubscriptionsManager.GetActiveSubscriptionsCount().Should().Be(1);
    }

    [Fact]
    public async Task Handle_NoActiveSubscriptions_ErrorMessageContainsContext()
    {
        var handler = CreateHandler(1);
        var act = async () => await handler.Handle(
            new ChangeUsersInSubscriptionCommand { UserIds = [10] },
            CancellationToken.None);
        var ex = await act.Should().ThrowAsync<RpcException>();
        ex.Which.Status.Detail.Should().Contain("No active subscriptions");
    }
}
