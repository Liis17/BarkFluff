using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Onliner.Features.SetOnlineStatus;
using BarkFluff.Onliner.Services;
using Grpc.Core;

namespace BarkFluff.Onliner.Tests.Features.SetOnlineStatus;

public class SetOnlineStatusCommandHandlerTests
{
    private readonly TestHelper _h = new();

    private SetOnlineStatusCommandHandler CreateHandler(long userId)
    {
        return new SetOnlineStatusCommandHandler(
            _h.CreateUserContext(userId),
            _h.Storage,
            _h.Notifier,
            _h.Metrics,
            TestHelper.CreateLogger<SetOnlineStatusCommandHandler>());
    }

    [Fact]
    public async Task Handle_NewUser_ReturnsSuccess()
    {
        var handler = CreateHandler(1);
        var result = await handler.Handle(new SetOnlineStatusCommand(), CancellationToken.None);
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task Handle_NewUser_SetsStatusOnline()
    {
        var handler = CreateHandler(1);
        await handler.Handle(new SetOnlineStatusCommand(), CancellationToken.None);
        var status = _h.Storage.GetStatus(1);
        status.Should().NotBeNull();
        status!.Status.Should().Be(DomainStatusTypeId.Online);
    }

    [Fact]
    public async Task Handle_StatusChanged_NotifiesSubscribers()
    {
        var stream = new Mock<IServerStreamWriter<ProtoUserOnlineStatus>>();
        _h.SubscriptionsManager.RegisterSubscription(10, [1], stream.Object);
        var handler = CreateHandler(1);
        await handler.Handle(new SetOnlineStatusCommand(), CancellationToken.None);
        stream.Verify(
            s => s.WriteAsync(
                It.Is<ProtoUserOnlineStatus>(m => m.UserId == 1),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_StatusChanged_IncrementsOnlineMetric()
    {
        var handler = CreateHandler(1);
        await handler.Handle(new SetOnlineStatusCommand(), CancellationToken.None);
        var snapshot = _h.Metrics.SnapshotAndReset();
        snapshot.Should().ContainKey("status_changes.online");
        snapshot["status_changes.online"].Should().Be(1);
    }

    [Fact]
    public async Task Handle_StatusNotChanged_DoesNotNotify()
    {
        var stream = new Mock<IServerStreamWriter<ProtoUserOnlineStatus>>();
        _h.SubscriptionsManager.RegisterSubscription(10, [1], stream.Object);
        var handler = CreateHandler(1);
        await handler.Handle(new SetOnlineStatusCommand(), CancellationToken.None);
        stream.Invocations.Clear();
        await handler.Handle(new SetOnlineStatusCommand(), CancellationToken.None);
        stream.Verify(
            s => s.WriteAsync(It.IsAny<ProtoUserOnlineStatus>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_StatusNotChanged_DoesNotIncrementOnlineMetric()
    {
        var handler = CreateHandler(1);
        await handler.Handle(new SetOnlineStatusCommand(), CancellationToken.None);
        _h.Metrics.SnapshotAndReset();
        await handler.Handle(new SetOnlineStatusCommand(), CancellationToken.None);
        var snapshot = _h.Metrics.SnapshotAndReset();
        snapshot.Should().NotContainKey("status_changes.online");
    }

    [Fact]
    public async Task Handle_TransitionOfflineToOnline_NotifiesSubscribers()
    {
        var stream = new Mock<IServerStreamWriter<ProtoUserOnlineStatus>>();
        _h.SubscriptionsManager.RegisterSubscription(10, [1], stream.Object);
        var handler = CreateHandler(1);
        await handler.Handle(new SetOnlineStatusCommand(), CancellationToken.None);
        _h.Storage.SetOffline(1);
        stream.Invocations.Clear();
        await handler.Handle(new SetOnlineStatusCommand(), CancellationToken.None);
        stream.Verify(
            s => s.WriteAsync(It.IsAny<ProtoUserOnlineStatus>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_TransitionOfflineToOnline_IncrementsOnlineMetric()
    {
        var handler = CreateHandler(1);
        await handler.Handle(new SetOnlineStatusCommand(), CancellationToken.None);
        _h.Storage.SetOffline(1);
        _h.Metrics.SnapshotAndReset();
        await handler.Handle(new SetOnlineStatusCommand(), CancellationToken.None);
        var snapshot = _h.Metrics.SnapshotAndReset();
        snapshot.Should().ContainKey("status_changes.online");
    }

    [Fact]
    public async Task Handle_UsesUserIdFromUserContext()
    {
        var handler = CreateHandler(42);
        await handler.Handle(new SetOnlineStatusCommand(), CancellationToken.None);
        _h.Storage.GetStatus(42).Should().NotBeNull();
        _h.Storage.GetStatus(1).Should().BeNull();
    }

    [Fact]
    public async Task Handle_NoSubscribers_DoesNotThrow()
    {
        var handler = CreateHandler(1);
        var act = async () => await handler.Handle(new SetOnlineStatusCommand(), CancellationToken.None);
        await act.Should().NotThrowAsync();
    }
}
