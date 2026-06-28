using BarkFluff.Onliner.BackgroundServices;
using BarkFluff.Onliner.Services;
using Grpc.Core;

namespace BarkFluff.Onliner.Tests.BackgroundServices;

public class OfflineDetectionServiceEdgeCaseTests
{
    private readonly TestHelper _h = new();

    [Fact]
    public async Task ExecuteAsync_UserAlreadyOffline_NoNotification()
    {
        _h.Storage.UpdateStatus(1);
        _h.Storage.SetOffline(1);

        var stream = new Mock<IServerStreamWriter<ProtoUserOnlineStatus>>();
        _h.SubscriptionsManager.RegisterSubscription(10, [1], stream.Object);

        using var cts = new CancellationTokenSource();
        var service = new TestableOfflineDetectionService(
            _h.Storage, _h.Notifier, TestHelper.CreateLogger<OfflineDetectionService>(), _h.Metrics);

        var task = service.RunAsync(cts.Token);
        await Task.Delay(TimeSpan.FromSeconds(7));
        cts.Cancel();

        stream.Verify(
            s => s.WriteAsync(It.IsAny<ProtoUserOnlineStatus>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private class TestableOfflineDetectionService : OfflineDetectionService
    {
        public TestableOfflineDetectionService(
            OnlineStatusStorage storage,
            OnlineStatusNotifier notifier,
            ILogger<OfflineDetectionService> logger,
            MetricsCollector metrics)
            : base(storage, notifier, logger, metrics) { }

        public Task RunAsync(CancellationToken ct) => ExecuteAsync(ct);
    }
}
