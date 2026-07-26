using System.Reflection;

using BarkFluff.Onliner.BackgroundServices;
using BarkFluff.Onliner.Messages;

using MassTransit;

using Microsoft.EntityFrameworkCore;

namespace BarkFluff.Onliner.Tests.BackgroundServices;

public class OfflineDetectionServiceTests
{
    private static readonly MethodInfo SweepMethod =
        typeof(OfflineDetectionService).GetMethod("CheckAndUpdateOfflineStatusesAsync",
            BindingFlags.NonPublic | BindingFlags.Instance)!;

    private readonly TestHelper _h = new();
    private readonly List<OnlineStatusChangedEvent> _published = [];
    private readonly Mock<IBus> _bus = new();

    public OfflineDetectionServiceTests()
    {
        _bus
            .Setup(b => b.Publish(It.IsAny<OnlineStatusChangedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<OnlineStatusChangedEvent, CancellationToken>((e, _) => _published.Add(e))
            .Returns(Task.CompletedTask);
    }

    private OfflineDetectionService CreateService()
    {
        return new OfflineDetectionService(
            _h.Presence,
            TestHelper.CreateSingleRunner(),
            _bus.Object,
            _h.CreateScopeFactory(),
            TestHelper.CreateLogger<OfflineDetectionService>(),
            _h.Metrics);
    }

    private static Task SweepAsync(OfflineDetectionService service)
        => (Task)SweepMethod.Invoke(service, [CancellationToken.None])!;

    [Fact]
    public async Task Sweep_StaleUser_MarkedOfflineAndPublished()
    {
        await _h.Presence.MarkOnlineAsync(1);
        _h.Presence.SetLastSeen(1, DateTime.UtcNow.AddSeconds(-10));

        await SweepAsync(CreateService());

        (await _h.Presence.GetOnlineAsync(1)).Should().BeNull();
        _published.Should().ContainSingle(e => e.UserId == 1 && e.Status == (int)DomainStatusTypeId.Offline);

        var dbStatus = await _h.DbContext.UsersOnlineStatuses.FindAsync(1L);
        dbStatus.Should().NotBeNull();
        dbStatus!.Status.Should().Be(DomainStatusTypeId.Offline);
    }

    [Fact]
    public async Task Sweep_StaleUser_IncrementsOfflineMetric()
    {
        await _h.Presence.MarkOnlineAsync(1);
        _h.Presence.SetLastSeen(1, DateTime.UtcNow.AddSeconds(-10));

        await SweepAsync(CreateService());

        var snapshot = _h.Metrics.SnapshotAndReset();
        snapshot.Should().ContainKey("status_changes.offline");
    }

    [Fact]
    public async Task Sweep_RecentUser_NotMarkedOffline()
    {
        await _h.Presence.MarkOnlineAsync(1);

        await SweepAsync(CreateService());

        (await _h.Presence.GetOnlineAsync(1)).Should().NotBeNull();
        _published.Should().BeEmpty();
    }

    [Fact]
    public async Task Sweep_NoUsers_NoPublishNoError()
    {
        var act = async () => await SweepAsync(CreateService());
        await act.Should().NotThrowAsync();
        _published.Should().BeEmpty();
    }
}
