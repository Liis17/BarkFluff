using BarkFluff.FastAuth.Features.SubscribeFastAuthResult;
using BarkFluff.Proto.FastAuth;
using BarkFluff.Shared.Exceptions.FastAuth;
using Grpc.Core;

namespace BarkFluff.FastAuth.Tests.Features.SubscribeFastAuthResult;

public class SubscribeFastAuthResultQueryHandlerTests
{
    private readonly TestHelper _h = new();

    private SubscribeFastAuthResultQueryHandler CreateHandler()
    {
        return new SubscribeFastAuthResultQueryHandler(
            _h.Store,
            _h.EventBus,
            _h.Metrics,
            TestHelper.CreateLogger<SubscribeFastAuthResultQueryHandler>());
    }

    private SubscribeFastAuthResultQuery CreateQuery(
        string fastAuthId,
        IServerStreamWriter<FastAuthResult> responseStream,
        CancellationToken cancellationToken = default)
    {
        return new SubscribeFastAuthResultQuery
        {
            FastAuthId = fastAuthId,
            ResponseStream = responseStream,
            CancellationToken = cancellationToken
        };
    }

    /// <summary>Имитирует подтверждение на другом инстансе: переход в сторе + публикация события.</summary>
    private async Task AcceptSessionRemotelyAsync(string sessionId, string code, long userId = 42)
    {
        await _h.Store.TryAcceptAsync(sessionId, code, userId, new BarkFluff.FastAuth.Domain.FastAuthSessionResult(
            FastAuthStatus.Accepted, "access", DateTime.UtcNow.AddHours(1), "refresh", DateTime.UtcNow.AddDays(30)));
        await _h.EventBus.PublishAsync(sessionId, new FastAuthResult
        {
            Status = FastAuthStatus.Accepted,
            AccessToken = "access",
            RefreshToken = "refresh"
        });
    }

    #region Success

    [Fact]
    public async Task Handle_ValidSubscription_IncrementsActiveSubscriptionsMetric()
    {
        var session = _h.CreateSession();
        var handler = CreateHandler();
        var cts = new CancellationTokenSource();
        var stream = _h.CreateMockStreamWriter();

        var handleTask = handler.Handle(CreateQuery(session.Id, stream.Object, cts.Token));

        await Task.Delay(50);
        var snapshot = _h.Metrics.SnapshotAndReset();
        snapshot.Should().ContainKey("active_subscriptions");

        await ExpireSessionRemotelyAsync(session.Id);
        await handleTask;
    }

    [Fact]
    public async Task Handle_RemoteAccept_WritesScannedThenAcceptedEventsToStream()
    {
        var session = _h.CreateSession();
        var handler = CreateHandler();
        var cts = new CancellationTokenSource();
        var stream = _h.CreateMockStreamWriter();

        var handleTask = handler.Handle(CreateQuery(session.Id, stream.Object, cts.Token));

        await Task.Delay(50);
        var code = Guid.NewGuid().ToString();
        await _h.Store.TryScanAsync(session.Id, 42, code);
        await _h.EventBus.PublishAsync(session.Id, new FastAuthResult { Status = FastAuthStatus.Scanned });
        await AcceptSessionRemotelyAsync(session.Id, code);
        await handleTask;

        stream.Verify(
            s => s.WriteAsync(
                It.Is<FastAuthResult>(r => r.Status == FastAuthStatus.Scanned),
                It.IsAny<CancellationToken>()),
            Times.Once);
        stream.Verify(
            s => s.WriteAsync(
                It.Is<FastAuthResult>(r => r.Status == FastAuthStatus.Accepted && r.AccessToken == "access"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_RemoteReject_WritesRejectedEventToStream()
    {
        var (session, code) = await _h.CreateAndScanSessionAsync();
        var handler = CreateHandler();
        var cts = new CancellationTokenSource();
        var stream = _h.CreateMockStreamWriter();

        var handleTask = handler.Handle(CreateQuery(session.Id, stream.Object, cts.Token));

        await Task.Delay(50);
        await _h.Store.TryRejectAsync(session.Id, code, 42);
        await _h.EventBus.PublishAsync(session.Id, new FastAuthResult { Status = FastAuthStatus.Rejected });
        await handleTask;

        stream.Verify(
            s => s.WriteAsync(
                It.Is<FastAuthResult>(r => r.Status == FastAuthStatus.Rejected),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_Completion_IncrementsClosedSubscriptionsMetric()
    {
        var session = _h.CreateSession();
        var handler = CreateHandler();
        var cts = new CancellationTokenSource();
        var stream = _h.CreateMockStreamWriter();

        var handleTask = handler.Handle(CreateQuery(session.Id, stream.Object, cts.Token));

        await Task.Delay(50);
        _h.Metrics.SnapshotAndReset();

        await ExpireSessionRemotelyAsync(session.Id);
        await handleTask;

        var snapshot = _h.Metrics.SnapshotAndReset();
        snapshot.Should().ContainKey("active_subscriptions_closed");
    }

    [Fact]
    public async Task Handle_Completion_ReleasesSubscriberLock()
    {
        var (session, code) = await _h.CreateAndScanSessionAsync();
        var handler = CreateHandler();
        var cts = new CancellationTokenSource();
        var stream = _h.CreateMockStreamWriter();

        var handleTask = handler.Handle(CreateQuery(session.Id, stream.Object, cts.Token));

        await Task.Delay(50);
        await AcceptSessionRemotelyAsync(session.Id, code);
        await handleTask;

        _h.Store.IsSubscriberAttached(session.Id).Should().BeFalse();
    }

    #endregion

    #region Deadline (QR expired)

    [Fact]
    public async Task Handle_DeadlineWithoutEvent_WritesExpiredEventToStream()
    {
        // Истекает почти сразу: локальный дедлайн срабатывает без sweeper'а.
        var session = _h.CreateSession(expiresAt: DateTime.UtcNow + TimeSpan.FromMilliseconds(400));
        var handler = CreateHandler();
        var stream = _h.CreateMockStreamWriter();

        await handler.Handle(CreateQuery(session.Id, stream.Object));

        stream.Verify(
            s => s.WriteAsync(
                It.Is<FastAuthResult>(r => r.Status == FastAuthStatus.Expired),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_AlreadyExpiredSession_WritesExpiredEventToStream()
    {
        var session = _h.CreateSession(expiresAt: DateTime.UtcNow - TimeSpan.FromSeconds(1));
        var handler = CreateHandler();
        var stream = _h.CreateMockStreamWriter();

        await handler.Handle(CreateQuery(session.Id, stream.Object));

        stream.Verify(
            s => s.WriteAsync(
                It.Is<FastAuthResult>(r => r.Status == FastAuthStatus.Expired),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_Deadline_MarksSessionExpiredInStore()
    {
        var session = _h.CreateSession(expiresAt: DateTime.UtcNow + TimeSpan.FromMilliseconds(400));
        var handler = CreateHandler();
        var stream = _h.CreateMockStreamWriter();

        await handler.Handle(CreateQuery(session.Id, stream.Object));

        var stored = await _h.Store.GetAsync(session.Id);
        stored!.Status.Should().Be(FastAuthStatus.Expired);
    }

    #endregion

    #region Reconnect / final session

    [Fact]
    public async Task Handle_AlreadyAcceptedSession_WritesStoredResultImmediately()
    {
        var (session, code) = await _h.CreateAndScanSessionAsync();
        await AcceptSessionRemotelyAsync(session.Id, code);
        var handler = CreateHandler();
        var stream = _h.CreateMockStreamWriter();

        await handler.Handle(CreateQuery(session.Id, stream.Object));

        stream.Verify(
            s => s.WriteAsync(
                It.Is<FastAuthResult>(r => r.Status == FastAuthStatus.Accepted && r.AccessToken == "access"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion

    #region Session Not Found

    [Fact]
    public async Task Handle_NonexistentSession_ThrowsFastAuthSessionNotFoundException()
    {
        var handler = CreateHandler();
        var stream = _h.CreateMockStreamWriter();

        var act = () => handler.Handle(CreateQuery("nonexistent", stream.Object));

        await act.Should().ThrowAsync<FastAuthSessionNotFoundException>();
    }

    #endregion

    #region Already Subscribed

    [Fact]
    public async Task Handle_AlreadySubscribed_ThrowsFastAuthInvalidStateException()
    {
        var session = _h.CreateSession();
        var handler = CreateHandler();

        var stream1 = _h.CreateMockStreamWriter();
        var cts = new CancellationTokenSource();
        var firstSubscription = handler.Handle(CreateQuery(session.Id, stream1.Object, cts.Token));

        await Task.Delay(50);

        var stream2 = _h.CreateMockStreamWriter();
        var act = () => handler.Handle(CreateQuery(session.Id, stream2.Object));
        await act.Should().ThrowAsync<FastAuthInvalidStateException>();

        await ExpireSessionRemotelyAsync(session.Id);
        await firstSubscription;
    }

    [Fact]
    public async Task Handle_AfterFirstSubscriberDisconnected_AllowsResubscribe()
    {
        var session = _h.CreateSession();
        var handler = CreateHandler();

        var stream1 = _h.CreateMockStreamWriter();
        var cts = new CancellationTokenSource();
        var firstSubscription = handler.Handle(CreateQuery(session.Id, stream1.Object, cts.Token));

        await Task.Delay(50);
        cts.Cancel();
        await firstSubscription;

        // Реконнект: захват освободился, сессия ещё жива — подписка возможна.
        var stream2 = _h.CreateMockStreamWriter();
        var secondSubscription = handler.Handle(CreateQuery(session.Id, stream2.Object));
        await Task.Delay(50);

        await ExpireSessionRemotelyAsync(session.Id);
        await secondSubscription;
    }

    #endregion

    #region Cancellation

    [Fact]
    public async Task Handle_Cancellation_CompletesGracefully()
    {
        var session = _h.CreateSession();
        var handler = CreateHandler();
        var stream = _h.CreateMockStreamWriter();

        var cts = new CancellationTokenSource();
        var handleTask = handler.Handle(CreateQuery(session.Id, stream.Object, cts.Token));

        await Task.Delay(50);
        cts.Cancel();

        await handleTask;
    }

    [Fact]
    public async Task Handle_Cancellation_IncrementsClosedMetric()
    {
        var session = _h.CreateSession();
        var handler = CreateHandler();
        var stream = _h.CreateMockStreamWriter();

        var cts = new CancellationTokenSource();
        var handleTask = handler.Handle(CreateQuery(session.Id, stream.Object, cts.Token));

        await Task.Delay(50);
        _h.Metrics.SnapshotAndReset();

        cts.Cancel();
        await handleTask;

        var snapshot = _h.Metrics.SnapshotAndReset();
        snapshot.Should().ContainKey("active_subscriptions_closed");
    }

    #endregion

    private async Task ExpireSessionRemotelyAsync(string sessionId)
    {
        await _h.Store.TryExpireAsync(sessionId);
        await _h.EventBus.PublishAsync(sessionId, new FastAuthResult { Status = FastAuthStatus.Expired });
    }
}
