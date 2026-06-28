using BarkFluff.Onliner.BackgroundServices;
using BarkFluff.Onliner.Services;

namespace BarkFluff.Onliner.Tests.BackgroundServices;

public class MetricsSnapshotServiceTests
{
    private readonly TestHelper _h = new();

    private TestableMetricsSnapshotService CreateService()
    {
        return new TestableMetricsSnapshotService(
            _h.Storage,
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

        var task = service.StartAsync(cts.Token);
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

        var task = service.StartAsync(cts.Token);
        await Task.Delay(500);
        cts.Cancel();

        var snapshot = _h.Metrics.SnapshotAndReset();
        snapshot.Should().ContainKey("tracked_unique_users");
        snapshot["tracked_unique_users"].Should().Be(2);
    }

    [Fact]
    public async Task ExecuteAsync_SetsOnlineUsersCountGauge()
    {
        _h.Storage.UpdateStatus(1);
        _h.Storage.UpdateStatus(2);
        _h.Storage.UpdateStatus(3);
        _h.Storage.SetOffline(3);

        var service = CreateService();
        using var cts = new CancellationTokenSource(500);

        var task = service.StartAsync(cts.Token);
        await Task.Delay(500);
        cts.Cancel();

        var snapshot = _h.Metrics.SnapshotAndReset();
        snapshot.Should().ContainKey("online_users_count");
        snapshot["online_users_count"].Should().Be(2);
    }

    [Fact]
    public async Task ExecuteAsync_SetsStorageTotalCountGauge()
    {
        _h.Storage.UpdateStatus(1);
        _h.Storage.UpdateStatus(2);

        var service = CreateService();
        using var cts = new CancellationTokenSource(500);

        var task = service.StartAsync(cts.Token);
        await Task.Delay(500);
        cts.Cancel();

        var snapshot = _h.Metrics.SnapshotAndReset();
        snapshot.Should().ContainKey("storage_total_count");
        snapshot["storage_total_count"].Should().Be(2);
    }

    [Fact]
    public async Task ExecuteAsync_EmptyState_SetsZeroCounts()
    {
        var service = CreateService();
        using var cts = new CancellationTokenSource(500);

        var task = service.StartAsync(cts.Token);
        await Task.Delay(500);
        cts.Cancel();

        var snapshot = _h.Metrics.SnapshotAndReset();
        snapshot["active_subscriptions"].Should().Be(0);
        snapshot["tracked_unique_users"].Should().Be(0);
        snapshot["online_users_count"].Should().Be(0);
        snapshot["storage_total_count"].Should().Be(0);
    }

    private class TestableMetricsSnapshotService : MetricsSnapshotService
    {
        public TestableMetricsSnapshotService(
            OnlineStatusStorage storage,
            OnlineStatusSubscriptionsManager subscriptionsManager,
            MetricsCollector metrics)
            : base(storage, subscriptionsManager, metrics) { }

        public new Task StartAsync(CancellationToken ct) => ExecuteAsync(ct);
    }
}
