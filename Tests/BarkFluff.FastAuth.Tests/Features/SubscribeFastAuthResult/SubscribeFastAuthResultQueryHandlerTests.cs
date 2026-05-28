using System.Threading.Channels;
using BarkFluff.FastAuth.Features.SubscribeFastAuthResult;
using BarkFluff.FastAuth.Infrastructure;
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
            _h.SessionsManager,
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

    #region Success

    [Fact]
    public async Task Handle_ValidSubscription_IncrementsActiveSubscriptionsMetric()
    {
        var session = _h.SessionsManager.Create("D", "OS", "A", "V", "IP");
        var handler = CreateHandler();
        var cts = new CancellationTokenSource();

        var stream = new Mock<IServerStreamWriter<FastAuthResult>>();
        stream
            .Setup(s => s.WriteAsync(It.IsAny<FastAuthResult>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var handleTask = handler.Handle(CreateQuery(session.Id, stream.Object, cts.Token));

        await Task.Delay(50);
        var snapshot = _h.Metrics.SnapshotAndReset();
        snapshot.Should().ContainKey("active_subscriptions");

        session.TryExpire();
        await handleTask;
    }

    [Fact]
    public async Task Handle_SessionExpired_WritesExpiredEventToStream()
    {
        var session = _h.SessionsManager.Create("D", "OS", "A", "V", "IP");
        var handler = CreateHandler();

        var stream = new Mock<IServerStreamWriter<FastAuthResult>>();
        stream
            .Setup(s => s.WriteAsync(It.IsAny<FastAuthResult>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var cts = new CancellationTokenSource();
        var handleTask = handler.Handle(CreateQuery(session.Id, stream.Object, cts.Token));

        await Task.Delay(50);
        session.TryExpire();
        await handleTask;

        stream.Verify(
            s => s.WriteAsync(
                It.Is<FastAuthResult>(r => r.Status == FastAuthStatus.Expired),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_SessionAccepted_WritesAcceptedEventToStream()
    {
        var session = _h.SessionsManager.Create("D", "OS", "A", "V", "IP");
        var handler = CreateHandler();

        var stream = new Mock<IServerStreamWriter<FastAuthResult>>();
        stream
            .Setup(s => s.WriteAsync(It.IsAny<FastAuthResult>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var cts = new CancellationTokenSource();
        var handleTask = handler.Handle(CreateQuery(session.Id, stream.Object, cts.Token));

        await Task.Delay(50);
        session.TryScan(42);
        session.TryAccept(session.ConfirmationCode!, 42, new FastAuthResult
        {
            Status = FastAuthStatus.Accepted,
            AccessToken = "access",
            RefreshToken = "refresh"
        });
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
    public async Task Handle_SessionRejected_WritesRejectedEventToStream()
    {
        var session = _h.SessionsManager.Create("D", "OS", "A", "V", "IP");
        var handler = CreateHandler();

        var stream = new Mock<IServerStreamWriter<FastAuthResult>>();
        stream
            .Setup(s => s.WriteAsync(It.IsAny<FastAuthResult>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var cts = new CancellationTokenSource();
        var handleTask = handler.Handle(CreateQuery(session.Id, stream.Object, cts.Token));

        await Task.Delay(50);
        session.TryScan(42);
        session.TryReject(session.ConfirmationCode!, 42);
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
        var session = _h.SessionsManager.Create("D", "OS", "A", "V", "IP");
        var handler = CreateHandler();

        var stream = new Mock<IServerStreamWriter<FastAuthResult>>();
        stream
            .Setup(s => s.WriteAsync(It.IsAny<FastAuthResult>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var cts = new CancellationTokenSource();
        var handleTask = handler.Handle(CreateQuery(session.Id, stream.Object, cts.Token));

        await Task.Delay(50);
        _h.Metrics.SnapshotAndReset();

        session.TryExpire();
        await handleTask;

        var snapshot = _h.Metrics.SnapshotAndReset();
        snapshot.Should().ContainKey("active_subscriptions_closed");
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
        var session = _h.SessionsManager.Create("D", "OS", "A", "V", "IP");
        var handler = CreateHandler();

        var stream1 = new Mock<IServerStreamWriter<FastAuthResult>>();
        stream1
            .Setup(s => s.WriteAsync(It.IsAny<FastAuthResult>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var cts = new CancellationTokenSource();
        var firstSubscription = handler.Handle(CreateQuery(session.Id, stream1.Object, cts.Token));

        await Task.Delay(50);

        var stream2 = _h.CreateMockStreamWriter();
        var act = () => handler.Handle(CreateQuery(session.Id, stream2.Object));
        await act.Should().ThrowAsync<FastAuthInvalidStateException>();

        session.TryExpire();
        await firstSubscription;
    }

    #endregion

    #region Cancellation

    [Fact]
    public async Task Handle_Cancellation_CompletesGracefully()
    {
        var session = _h.SessionsManager.Create("D", "OS", "A", "V", "IP");
        var handler = CreateHandler();

        var stream = new Mock<IServerStreamWriter<FastAuthResult>>();
        stream
            .Setup(s => s.WriteAsync(It.IsAny<FastAuthResult>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var cts = new CancellationTokenSource();
        var handleTask = handler.Handle(CreateQuery(session.Id, stream.Object, cts.Token));

        await Task.Delay(50);
        cts.Cancel();

        await handleTask;
    }

    [Fact]
    public async Task Handle_Cancellation_IncrementsClosedMetric()
    {
        var session = _h.SessionsManager.Create("D", "OS", "A", "V", "IP");
        var handler = CreateHandler();

        var stream = new Mock<IServerStreamWriter<FastAuthResult>>();
        stream
            .Setup(s => s.WriteAsync(It.IsAny<FastAuthResult>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

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
}
