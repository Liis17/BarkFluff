using BarkFluff.Onliner.Consumers;
using BarkFluff.Onliner.Features.SetOnlineStatus;
using BarkFluff.Onliner.Messages;

using Grpc.Core;

using MassTransit;

namespace BarkFluff.Onliner.Tests.Features.SetOnlineStatus;

public class SetOnlineStatusCommandHandlerTests
{
    private readonly TestHelper _h = new();
    private readonly List<OnlineStatusChangedEvent> _published = [];
    private readonly Mock<IPublishEndpoint> _publish = new();

    public SetOnlineStatusCommandHandlerTests()
    {
        _publish
            .Setup(p => p.Publish(It.IsAny<OnlineStatusChangedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<OnlineStatusChangedEvent, CancellationToken>((e, _) => _published.Add(e))
            .Returns(Task.CompletedTask);
    }

    private SetOnlineStatusCommandHandler CreateHandler(long userId)
    {
        return new SetOnlineStatusCommandHandler(
            _h.CreateUserContext(userId),
            _h.Presence,
            _publish.Object,
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
        var status = await _h.Presence.GetOnlineAsync(1);
        status.Should().NotBeNull();
        status!.Status.Should().Be(DomainStatusTypeId.Online);
    }

    [Fact]
    public async Task Handle_StatusChanged_PublishesOnlineEvent()
    {
        var handler = CreateHandler(1);
        await handler.Handle(new SetOnlineStatusCommand(), CancellationToken.None);
        _published.Should().ContainSingle(e => e.UserId == 1 && e.Status == (int)DomainStatusTypeId.Online);
    }

    [Fact]
    public async Task Handle_StatusChanged_DeliveredToSubscribersViaConsumer()
    {
        var stream = new Mock<IServerStreamWriter<ProtoUserOnlineStatus>>();
        _h.SubscriptionsManager.RegisterSubscription(10, [1], stream.Object);

        var handler = CreateHandler(1);
        await handler.Handle(new SetOnlineStatusCommand(), CancellationToken.None);

        // Fan-out консьюмер на инстансе подписчика доставляет опубликованное событие его стриму.
        var consumer = new OnlineStatusChangedConsumer(_h.Notifier);
        await consumer.Consume(ConsumeContextFor(_published.Single()));

        stream.Verify(
            s => s.WriteAsync(It.Is<ProtoUserOnlineStatus>(m => m.UserId == 1), It.IsAny<CancellationToken>()),
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
    public async Task Handle_StatusNotChanged_DoesNotPublish()
    {
        var handler = CreateHandler(1);
        await handler.Handle(new SetOnlineStatusCommand(), CancellationToken.None);
        _published.Clear();
        await handler.Handle(new SetOnlineStatusCommand(), CancellationToken.None);
        _published.Should().BeEmpty();
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
    public async Task Handle_TransitionOfflineToOnline_PublishesAgain()
    {
        var handler = CreateHandler(1);
        await handler.Handle(new SetOnlineStatusCommand(), CancellationToken.None);
        await _h.Presence.SetOfflineAsync(1);
        _published.Clear();
        await handler.Handle(new SetOnlineStatusCommand(), CancellationToken.None);
        _published.Should().ContainSingle(e => e.UserId == 1);
    }

    [Fact]
    public async Task Handle_UsesUserIdFromUserContext()
    {
        var handler = CreateHandler(42);
        await handler.Handle(new SetOnlineStatusCommand(), CancellationToken.None);
        (await _h.Presence.GetOnlineAsync(42)).Should().NotBeNull();
        (await _h.Presence.GetOnlineAsync(1)).Should().BeNull();
    }

    [Fact]
    public async Task Handle_NoSubscribers_DoesNotThrow()
    {
        var handler = CreateHandler(1);
        var act = async () => await handler.Handle(new SetOnlineStatusCommand(), CancellationToken.None);
        await act.Should().NotThrowAsync();
    }

    private static ConsumeContext<OnlineStatusChangedEvent> ConsumeContextFor(OnlineStatusChangedEvent message)
    {
        var context = new Mock<ConsumeContext<OnlineStatusChangedEvent>>();
        context.SetupGet(c => c.Message).Returns(message);
        context.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);
        return context.Object;
    }
}
