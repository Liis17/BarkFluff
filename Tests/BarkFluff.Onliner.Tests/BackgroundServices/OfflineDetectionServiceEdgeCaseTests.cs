using System.Reflection;

using BarkFluff.Onliner.BackgroundServices;
using BarkFluff.Onliner.Messages;

using MassTransit;

namespace BarkFluff.Onliner.Tests.BackgroundServices;

public class OfflineDetectionServiceEdgeCaseTests
{
    private static readonly MethodInfo SweepMethod =
        typeof(OfflineDetectionService).GetMethod("CheckAndUpdateOfflineStatusesAsync",
            BindingFlags.NonPublic | BindingFlags.Instance)!;

    private readonly TestHelper _h = new();
    private readonly List<OnlineStatusChangedEvent> _published = [];
    private readonly Mock<IBus> _bus = new();

    public OfflineDetectionServiceEdgeCaseTests()
    {
        _bus
            .Setup(b => b.Publish(It.IsAny<OnlineStatusChangedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<OnlineStatusChangedEvent, CancellationToken>((e, _) => _published.Add(e))
            .Returns(Task.CompletedTask);
    }

    [Fact]
    public async Task Sweep_UserAlreadyOffline_NoNotification()
    {
        // Пользователь уже не в presence (offline) — sweep не найдёт кандидатов, событий не будет.
        await _h.Presence.MarkOnlineAsync(1);
        await _h.Presence.SetOfflineAsync(1);

        var service = new OfflineDetectionService(
            _h.Presence,
            TestHelper.CreateSingleRunner(),
            _bus.Object,
            _h.CreateScopeFactory(),
            TestHelper.CreateLogger<OfflineDetectionService>(),
            _h.Metrics);

        await (Task)SweepMethod.Invoke(service, [CancellationToken.None])!;

        _published.Should().BeEmpty();
    }
}
