using BarkFluff.Onliner.Services;
using Grpc.Core;

namespace BarkFluff.Onliner.Tests.Services;

public class OnlineStatusNotifierEdgeCaseTests
{
    private readonly OnlineStatusSubscriptionsManager _subscriptionsManager = new();
    private readonly MetricsCollector _metrics = new();
    private readonly OnlineStatusNotifier _notifier;

    public OnlineStatusNotifierEdgeCaseTests()
    {
        _notifier = new OnlineStatusNotifier(
            _subscriptionsManager,
            _metrics,
            TestHelper.CreateLogger<OnlineStatusNotifier>());
    }

    [Fact]
    public async Task NotifyStatusChanged_UnknownStatus_MapsToProtoUnknown()
    {
        var stream = new Mock<IServerStreamWriter<ProtoUserOnlineStatus>>();
        _subscriptionsManager.RegisterSubscription(10, [1], stream.Object);

        var status = new DomainUserOnlineStatus
        {
            UserId = 1,
            Status = DomainStatusTypeId.Unknown,
            LastSeen = DateTime.UtcNow
        };

        await _notifier.NotifyStatusChanged(1, status);

        stream.Verify(
            s => s.WriteAsync(
                It.Is<ProtoUserOnlineStatus>(m => m.Status == ProtoStatusTypeId.Unknown),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task NotifyStatusChanged_SetsCorrectUserId()
    {
        var stream = new Mock<IServerStreamWriter<ProtoUserOnlineStatus>>();
        _subscriptionsManager.RegisterSubscription(10, [42], stream.Object);

        var status = new DomainUserOnlineStatus
        {
            UserId = 42,
            Status = DomainStatusTypeId.Online,
            LastSeen = DateTime.UtcNow
        };

        await _notifier.NotifyStatusChanged(42, status);

        stream.Verify(
            s => s.WriteAsync(
                It.Is<ProtoUserOnlineStatus>(m => m.UserId == 42),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
