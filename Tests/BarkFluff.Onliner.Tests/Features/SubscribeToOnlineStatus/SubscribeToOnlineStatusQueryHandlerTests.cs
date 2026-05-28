using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Onliner.Features.SubscribeToOnlineStatus;
using BarkFluff.Onliner.Services;
using BarkFluff.Proto.Users;
using Grpc.Core;

namespace BarkFluff.Onliner.Tests.Features.SubscribeToOnlineStatus;

public class SubscribeToOnlineStatusQueryHandlerTests
{
    private readonly TestHelper _h = new();

    private SubscribeToOnlineStatusQueryHandler CreateHandler(long userId)
    {
        return new SubscribeToOnlineStatusQueryHandler(
            _h.CreateUserContext(userId),
            _h.SubscriptionsManager,
            _h.CreateVisibilityFilter(),
            _h.Metrics,
            TestHelper.CreateLogger<SubscribeToOnlineStatusQueryHandler>());
    }

    private async Task RunHandlerWithCancellation(
        SubscribeToOnlineStatusQueryHandler handler,
        List<long> userIds,
        IServerStreamWriter<ProtoUserOnlineStatus> stream,
        int delayMs = 200)
    {
        using var cts = new CancellationTokenSource(delayMs);
        var query = new SubscribeToOnlineStatusQuery
        {
            UserIds = userIds,
            ResponseStream = stream,
            CancellationToken = cts.Token
        };
        await handler.Handle(query);
    }

    [Fact]
    public async Task Handle_RegistersAndRemovesSubscription()
    {
        _h.SetupUserPrivacy(10, ProfileFieldVisibility.All);
        var handler = CreateHandler(1);
        var stream = _h.CreateMockStreamWriter();

        await RunHandlerWithCancellation(handler, [10], stream.Object);

        _h.SubscriptionsManager.GetActiveSubscriptionsCount().Should().Be(0);
    }

    [Fact]
    public async Task Handle_DuringSubscription_SubscriptionIsActive()
    {
        _h.SetupUserPrivacy(10, ProfileFieldVisibility.All);
        var handler = CreateHandler(1);
        var stream = _h.CreateMockStreamWriter();
        using var cts = new CancellationTokenSource(500);
        var query = new SubscribeToOnlineStatusQuery
        {
            UserIds = [10],
            ResponseStream = stream.Object,
            CancellationToken = cts.Token
        };

        var handleTask = handler.Handle(query);
        await Task.Delay(50);
        _h.SubscriptionsManager.GetActiveSubscriptionsCount().Should().Be(1);
        cts.Cancel();
        try { await handleTask; } catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task Handle_FiltersHiddenUsers()
    {
        _h.SetupUserPrivacy(10, ProfileFieldVisibility.All);
        _h.SetupUserPrivacy(20, ProfileFieldVisibility.None);
        var handler = CreateHandler(1);
        var stream = _h.CreateMockStreamWriter();

        await RunHandlerWithCancellation(handler, [10, 20], stream.Object);

        var snapshot = _h.Metrics.SnapshotAndReset();
        snapshot.Should().ContainKey("subscriptions_hidden_by_privacy");
    }

    [Fact]
    public async Task Handle_AllHidden_StillRegistersSubscription()
    {
        _h.SetupUserPrivacy(10, ProfileFieldVisibility.None);
        _h.SetupUserPrivacy(20, ProfileFieldVisibility.None);
        var handler = CreateHandler(1);
        var stream = _h.CreateMockStreamWriter();
        using var cts = new CancellationTokenSource(500);
        var query = new SubscribeToOnlineStatusQuery
        {
            UserIds = [10, 20],
            ResponseStream = stream.Object,
            CancellationToken = cts.Token
        };

        var handleTask = handler.Handle(query);
        await Task.Delay(50);
        _h.SubscriptionsManager.GetActiveSubscriptionsCount().Should().Be(1);
        _h.SubscriptionsManager.GetTrackedUniqueUsersCount().Should().Be(0);
        cts.Cancel();
        try { await handleTask; } catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task Handle_Cancellation_RemovesSubscription()
    {
        _h.SetupUserPrivacy(10, ProfileFieldVisibility.All);
        var handler = CreateHandler(1);
        var stream = _h.CreateMockStreamWriter();
        using var cts = new CancellationTokenSource(500);
        var query = new SubscribeToOnlineStatusQuery
        {
            UserIds = [10],
            ResponseStream = stream.Object,
            CancellationToken = cts.Token
        };

        var handleTask = handler.Handle(query);
        await Task.Delay(50);
        _h.SubscriptionsManager.GetActiveSubscriptionsCount().Should().Be(1);
        cts.Cancel();
        try { await handleTask; } catch (OperationCanceledException) { }
        _h.SubscriptionsManager.GetActiveSubscriptionsCount().Should().Be(0);
    }

    [Fact]
    public async Task Handle_Cancellation_IncrementsDisconnectedMetric()
    {
        _h.SetupUserPrivacy(10, ProfileFieldVisibility.All);
        var handler = CreateHandler(1);
        var stream = _h.CreateMockStreamWriter();

        await RunHandlerWithCancellation(handler, [10], stream.Object);

        var snapshot = _h.Metrics.SnapshotAndReset();
        snapshot.Should().ContainKey("subscriptions_disconnected");
    }

    [Fact]
    public async Task Handle_SelfAlwaysIncluded()
    {
        _h.SetupUserPrivacy(1, ProfileFieldVisibility.None);
        var handler = CreateHandler(1);
        var stream = _h.CreateMockStreamWriter();
        using var cts = new CancellationTokenSource(500);
        var query = new SubscribeToOnlineStatusQuery
        {
            UserIds = [1],
            ResponseStream = stream.Object,
            CancellationToken = cts.Token
        };

        var handleTask = handler.Handle(query);
        await Task.Delay(50);
        _h.SubscriptionsManager.GetTrackedUniqueUsersCount().Should().Be(1);
        cts.Cancel();
        try { await handleTask; } catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task Handle_EmptyUserIds_RegistersEmptySubscription()
    {
        var handler = CreateHandler(1);
        var stream = _h.CreateMockStreamWriter();
        using var cts = new CancellationTokenSource(500);
        var query = new SubscribeToOnlineStatusQuery
        {
            UserIds = [],
            ResponseStream = stream.Object,
            CancellationToken = cts.Token
        };

        var handleTask = handler.Handle(query);
        await Task.Delay(50);
        _h.SubscriptionsManager.GetActiveSubscriptionsCount().Should().Be(1);
        _h.SubscriptionsManager.GetTrackedUniqueUsersCount().Should().Be(0);
        cts.Cancel();
        try { await handleTask; } catch (OperationCanceledException) { }
    }
}
