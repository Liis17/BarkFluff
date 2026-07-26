using BarkFluff.Onliner.BackgroundServices;
using BarkFluff.Onliner.Services;
using BarkFluff.Onliner.Tests.Fakes;

namespace BarkFluff.Onliner.Tests.BackgroundServices;

public class MetricsSnapshotServiceTests
{
    private readonly TestHelper _h = new();

    private TestableMetricsSnapshotService CreateService()
    {
        return new TestableMetricsSnapshotService(
            _h.Presence,
            _h.SubscriptionsManager,
            _h.Metrics);
    }

    [Fact]
    public async Task ExecuteAsync_SetsActiveSubscriptionsGauge()
    {
        var stream = _h.CreateMockStreamWriter();
        _h.SubscriptionsManager.RegisterSubscription(1, [10], stream.Object);

        var service = CreateService();
        using var cts = new CancellationTokenSource(500);

        _ = service.StartAsync(cts.Token);
        await Task.Delay(500);
        cts.Cancel();

        var snapshot = _h.Metrics.SnapshotAndReset();
        snapshot.Should().ContainKey("active_subscriptions");
        snapshot["active_subscriptions"].Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_SetsTrackedUniqueUsersGauge()
    {
        var stream = _h.CreateMockStreamWriter();
        _h.SubscriptionsManager.RegisterSubscription(1, [10, 20], stream.Object);

        var service = CreateService();
        using var cts = new CancellationTokenSource(500);

        _ = service.StartAsync(cts.Token);
        await Task.Delay(500);
        cts.Cancel();

        var snapshot = _h.Metrics.SnapshotAndReset();
        snapshot.Should().ContainKey("tracked_unique_users");
        snapshot["tracked_unique_users"].Should().Be(2);
    }

    [Fact]
    public async Task ExecuteAsync_SetsOnlineUsersCountGauge()
    {
        await _h.Presence.MarkOnlineAsync(1);
        await _h.Presence.MarkOnlineAsync(2);
        await _h.Presence.MarkOnlineAsync(3);
        await _h.Presence.SetOfflineAsync(3);

        var service = CreateService();
        using var cts = new CancellationTokenSource(500);

        _ = service.StartAsync(cts.Token);
        await Task.Delay(500);
        cts.Cancel();

        var snapshot = _h.Metrics.SnapshotAndReset();
        snapshot.Should().ContainKey("online_users_count");
        snapshot["online_users_count"].Should().Be(2);
    }

    [Fact]
    public async Task ExecuteAsync_EmptyState_SetsZeroCounts()
    {
        var service = CreateService();
        using var cts = new CancellationTokenSource(500);

        _ = service.StartAsync(cts.Token);
        await Task.Delay(500);
        cts.Cancel();

        var snapshot = _h.Metrics.SnapshotAndReset();
        snapshot["active_subscriptions"].Should().Be(0);
        snapshot["tracked_unique_users"].Should().Be(0);
        snapshot["online_users_count"].Should().Be(0);
    }

    private class TestableMetricsSnapshotService : MetricsSnapshotService
    {
        public TestableMetricsSnapshotService(
            IPresenceStore presence,
            OnlineStatusSubscriptionsManager subscriptionsManager,
            MetricsCollector metrics)
            : base(presence, subscriptionsManager, metrics) { }

        public new Task StartAsync(CancellationToken ct) => ExecuteAsync(ct);
    }
}
