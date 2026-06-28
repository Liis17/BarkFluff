using BarkFluff.Onliner.Services;
using Grpc.Core;

namespace BarkFluff.Onliner.Tests.Services;

public class OnlineStatusNotifierTests
{
    private readonly OnlineStatusSubscriptionsManager _subscriptionsManager = new();
    private readonly MetricsCollector _metrics = new();
    private readonly OnlineStatusNotifier _notifier;

    public OnlineStatusNotifierTests()
    {
        _notifier = new OnlineStatusNotifier(
            _subscriptionsManager,
            _metrics,
            TestHelper.CreateLogger<OnlineStatusNotifier>());
    }

    private static DomainUserOnlineStatus CreateDomainStatus(
        long userId, DomainStatusTypeId status = DomainStatusTypeId.Online)
    {
        return new DomainUserOnlineStatus
        {
            UserId = userId,
            Status = status,
            LastSeen = DateTime.UtcNow
        };
    }

    private Mock<IServerStreamWriter<ProtoUserOnlineStatus>> CreateStream()
    {
        return new Mock<IServerStreamWriter<ProtoUserOnlineStatus>>();
    }

    [Fact]
    public async Task NotifyStatusChanged_NoSubscribers_DoesNothing()
    {
        var status = CreateDomainStatus(1);
        await _notifier.NotifyStatusChanged(1, status);
    }

    [Fact]
    public async Task NotifyStatusChanged_WithSubscribers_SendsNotification()
    {
        var stream = CreateStream();
        _subscriptionsManager.RegisterSubscription(10, [1], stream.Object);
        var status = CreateDomainStatus(1);
        await _notifier.NotifyStatusChanged(1, status);
        stream.Verify(
            s => s.WriteAsync(
                It.Is<ProtoUserOnlineStatus>(m => m.UserId == 1 && m.Status == ProtoStatusTypeId.StatusOnline),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task NotifyStatusChanged_MapsOnlineStatusCorrectly()
    {
        var stream = CreateStream();
        _subscriptionsManager.RegisterSubscription(10, [1], stream.Object);
        var status = CreateDomainStatus(1, DomainStatusTypeId.Online);
        await _notifier.NotifyStatusChanged(1, status);
        stream.Verify(
            s => s.WriteAsync(
                It.Is<ProtoUserOnlineStatus>(m => m.Status == ProtoStatusTypeId.StatusOnline),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task NotifyStatusChanged_MapsOfflineStatusCorrectly()
    {
        var stream = CreateStream();
        _subscriptionsManager.RegisterSubscription(10, [1], stream.Object);
        var status = CreateDomainStatus(1, DomainStatusTypeId.Offline);
        await _notifier.NotifyStatusChanged(1, status);
        stream.Verify(
            s => s.WriteAsync(
                It.Is<ProtoUserOnlineStatus>(m => m.Status == ProtoStatusTypeId.StatusOffline),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task NotifyStatusChanged_SetsLastSeen()
    {
        var stream = CreateStream();
        _subscriptionsManager.RegisterSubscription(10, [1], stream.Object);
        var now = DateTime.UtcNow;
        var status = CreateDomainStatus(1) with { LastSeen = now };
        await _notifier.NotifyStatusChanged(1, status);
        stream.Verify(
            s => s.WriteAsync(
                It.Is<ProtoUserOnlineStatus>(m => m.LastSeen.ToDateTime().ToUniversalTime() == now.ToUniversalTime()),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task NotifyStatusChanged_MultipleSubscribers_AllReceiveNotification()
    {
        var stream1 = CreateStream();
        var stream2 = CreateStream();
        var stream3 = CreateStream();
        _subscriptionsManager.RegisterSubscription(10, [1], stream1.Object);
        _subscriptionsManager.RegisterSubscription(20, [1], stream2.Object);
        _subscriptionsManager.RegisterSubscription(30, [1], stream3.Object);
        var status = CreateDomainStatus(1);
        await _notifier.NotifyStatusChanged(1, status);
        stream1.Verify(s => s.WriteAsync(It.IsAny<ProtoUserOnlineStatus>(), It.IsAny<CancellationToken>()), Times.Once);
        stream2.Verify(s => s.WriteAsync(It.IsAny<ProtoUserOnlineStatus>(), It.IsAny<CancellationToken>()), Times.Once);
        stream3.Verify(s => s.WriteAsync(It.IsAny<ProtoUserOnlineStatus>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task NotifyStatusChanged_SubscriberNotTrackingUser_DoesNotReceive()
    {
        var stream = CreateStream();
        _subscriptionsManager.RegisterSubscription(10, [999], stream.Object);
        var status = CreateDomainStatus(1);
        await _notifier.NotifyStatusChanged(1, status);
        stream.Verify(s => s.WriteAsync(It.IsAny<ProtoUserOnlineStatus>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task NotifyStatusChanged_StreamError_IncrementsErrorMetric()
    {
        var stream = CreateStream();
        stream.Setup(s => s.WriteAsync(It.IsAny<ProtoUserOnlineStatus>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Stream closed"));
        _subscriptionsManager.RegisterSubscription(10, [1], stream.Object);
        var status = CreateDomainStatus(1);
        await _notifier.NotifyStatusChanged(1, status);
        var snapshot = _metrics.SnapshotAndReset();
        snapshot.Should().ContainKey("status_notification_errors");
    }

    [Fact]
    public async Task NotifyStatusChanged_Success_IncrementsSentMetric()
    {
        var stream = CreateStream();
        _subscriptionsManager.RegisterSubscription(10, [1], stream.Object);
        var status = CreateDomainStatus(1);
        await _notifier.NotifyStatusChanged(1, status);
        var snapshot = _metrics.SnapshotAndReset();
        snapshot.Should().ContainKey("status_notifications_sent");
    }

    [Fact]
    public async Task NotifyStatusChanged_OneStreamFails_OthersStillReceive()
    {
        var goodStream = CreateStream();
        var badStream = CreateStream();
        badStream.Setup(s => s.WriteAsync(It.IsAny<ProtoUserOnlineStatus>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("broken"));
        _subscriptionsManager.RegisterSubscription(10, [1], goodStream.Object);
        _subscriptionsManager.RegisterSubscription(20, [1], badStream.Object);
        var status = CreateDomainStatus(1);
        await _notifier.NotifyStatusChanged(1, status);
        goodStream.Verify(s => s.WriteAsync(It.IsAny<ProtoUserOnlineStatus>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
