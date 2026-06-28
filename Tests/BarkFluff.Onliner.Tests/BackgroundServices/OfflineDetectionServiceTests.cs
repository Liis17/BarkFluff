using BarkFluff.Onliner.BackgroundServices;
using BarkFluff.Onliner.Services;
using Grpc.Core;

namespace BarkFluff.Onliner.Tests.BackgroundServices;

public class OfflineDetectionServiceTests
{
    private readonly TestHelper _h = new();

    private TestableOfflineDetectionService CreateService()
    {
        return new TestableOfflineDetectionService(
            _h.Storage,
            _h.Notifier,
            TestHelper.CreateLogger<OfflineDetectionService>(),
            _h.Metrics);
    }

    [Fact]
    public async Task ExecuteAsync_MarksOfflineUsersAsOffline()
    {
        _h.Storage.UpdateStatus(1);
        _h.Storage.UpdateStatus(2);

        using var cts = new CancellationTokenSource();
        var service = CreateService();
        var task = service.StartAsync(cts.Token);

        await Task.Delay(TimeSpan.FromSeconds(7));
        cts.Cancel();

        var status1 = _h.Storage.GetStatus(1);
        var status2 = _h.Storage.GetStatus(2);
        status1!.Status.Should().Be(DomainStatusTypeId.Offline);
        status2!.Status.Should().Be(DomainStatusTypeId.Offline);
    }

    [Fact]
    public async Task ExecuteAsync_NotifiesSubscribersOnOffline()
    {
        _h.Storage.UpdateStatus(1);
        var stream = new Mock<IServerStreamWriter<ProtoUserOnlineStatus>>();
        _h.SubscriptionsManager.RegisterSubscription(10, [1], stream.Object);

        using var cts = new CancellationTokenSource();
        var service = CreateService();
        var task = service.StartAsync(cts.Token);

        await Task.Delay(TimeSpan.FromSeconds(7));
        cts.Cancel();

        stream.Verify(
            s => s.WriteAsync(
                It.Is<ProtoUserOnlineStatus>(m => m.UserId == 1 && m.Status == ProtoStatusTypeId.StatusOffline),
                It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task ExecuteAsync_IncrementsOfflineDetectionRuns()
    {
        using var cts = new CancellationTokenSource(1500);
        var service = CreateService();
        var task = service.StartAsync(cts.Token);

        await Task.Delay(1500);
        cts.Cancel();

        var snapshot = _h.Metrics.SnapshotAndReset();
        snapshot.Should().ContainKey("offline_detection_runs");
    }

    [Fact]
    public async Task ExecuteAsync_DoesNotMarkRecentOnlineUser()
    {
        using var cts = new CancellationTokenSource();
        var service = CreateService();
        var task = service.StartAsync(cts.Token);

        await Task.Delay(1500);
        _h.Storage.UpdateStatus(1);
        await Task.Delay(1500);
        cts.Cancel();

        var status = _h.Storage.GetStatus(1);
        status!.Status.Should().Be(DomainStatusTypeId.Online);
    }

    [Fact]
    public async Task ExecuteAsync_IncrementsStatusChangesOffline()
    {
        _h.Storage.UpdateStatus(1);

        using var cts = new CancellationTokenSource();
        var service = CreateService();
        var task = service.StartAsync(cts.Token);

        await Task.Delay(TimeSpan.FromSeconds(7));
        cts.Cancel();

        var snapshot = _h.Metrics.SnapshotAndReset();
        snapshot.Should().ContainKey("status_changes.offline");
    }

    [Fact]
    public async Task ExecuteAsync_NoOnlineUsers_RunsDetectionWithoutErrors()
    {
        using var cts = new CancellationTokenSource(1500);
        var service = CreateService();
        var task = service.StartAsync(cts.Token);

        await Task.Delay(1500);
        cts.Cancel();

        var snapshot = _h.Metrics.SnapshotAndReset();
        snapshot.Should().ContainKey("offline_detection_runs");
        snapshot.Should().NotContainKey("offline_detection_errors");
    }

    private class TestableOfflineDetectionService : OfflineDetectionService
    {
        public TestableOfflineDetectionService(
            OnlineStatusStorage storage,
            OnlineStatusNotifier notifier,
            ILogger<OfflineDetectionService> logger,
            MetricsCollector metrics)
            : base(storage, notifier, logger, metrics) { }

        public new Task StartAsync(CancellationToken ct) => ExecuteAsync(ct);
    }
}
