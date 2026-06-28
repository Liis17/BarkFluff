using BarkFluff.FastAuth.Domain;
using BarkFluff.FastAuth.Features.RejectFastAuth;
using BarkFluff.FastAuth.Infrastructure;
using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Proto.FastAuth;
using BarkFluff.Shared.Exceptions.FastAuth;

namespace BarkFluff.FastAuth.Tests.Features.RejectFastAuth;

public class RejectFastAuthCommandHandlerTests
{
    private readonly TestHelper _h = new();

    private RejectFastAuthCommandHandler CreateHandler(long userId = 42)
    {
        return new RejectFastAuthCommandHandler(
            _h.SessionsManager,
            _h.CreateUserContext(userId),
            _h.Metrics,
            TestHelper.CreateLogger<RejectFastAuthCommandHandler>());
    }

    private (FastAuthSession session, string code) CreateAndScanSession(long userId = 42)
    {
        var session = _h.SessionsManager.Create("D", "OS", "A", "V", "IP");
        session.TryScan(userId);
        return (session, session.ConfirmationCode!);
    }

    #region Success

    [Fact]
    public async Task Handle_ValidRequest_ReturnsResponse()
    {
        var (session, code) = CreateAndScanSession();
        var handler = CreateHandler();

        var result = await handler.Handle(
            new RejectFastAuthCommand { FastAuthId = session.Id, ConfirmationCode = code },
            CancellationToken.None);

        result.Should().NotBeNull();
    }

    [Fact]
    public async Task Handle_ValidRequest_SetsSessionStatusToRejected()
    {
        var (session, code) = CreateAndScanSession();
        var handler = CreateHandler();

        await handler.Handle(
            new RejectFastAuthCommand { FastAuthId = session.Id, ConfirmationCode = code },
            CancellationToken.None);

        session.Status.Should().Be(FastAuthStatus.Rejected);
    }

    [Fact]
    public async Task Handle_ValidRequest_MakesSessionFinal()
    {
        var (session, code) = CreateAndScanSession();
        var handler = CreateHandler();

        await handler.Handle(
            new RejectFastAuthCommand { FastAuthId = session.Id, ConfirmationCode = code },
            CancellationToken.None);

        session.IsFinal.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_ValidRequest_WritesRejectedEventToChannel()
    {
        var (session, code) = CreateAndScanSession();
        var handler = CreateHandler();

        await handler.Handle(
            new RejectFastAuthCommand { FastAuthId = session.Id, ConfirmationCode = code },
            CancellationToken.None);

        session.Events.TryRead(out var scanEvt).Should().BeTrue();
        session.Events.TryRead(out var rejectEvt).Should().BeTrue();
        rejectEvt.Status.Should().Be(FastAuthStatus.Rejected);
    }

    [Fact]
    public async Task Handle_ValidRequest_CompletesChannel()
    {
        var (session, code) = CreateAndScanSession();
        var handler = CreateHandler();

        await handler.Handle(
            new RejectFastAuthCommand { FastAuthId = session.Id, ConfirmationCode = code },
            CancellationToken.None);

        while (session.Events.TryRead(out _)) { }
        session.Events.Completion.IsCompleted.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_ValidRequest_IncrementsSessionsRejectedMetric()
    {
        var (session, code) = CreateAndScanSession();
        var handler = CreateHandler();

        await handler.Handle(
            new RejectFastAuthCommand { FastAuthId = session.Id, ConfirmationCode = code },
            CancellationToken.None);

        var snapshot = _h.Metrics.SnapshotAndReset();
        snapshot.Should().ContainKey("sessions_rejected");
        snapshot["sessions_rejected"].Should().Be(1);
    }

    #endregion

    #region Session Not Found

    [Fact]
    public async Task Handle_NonexistentSession_ThrowsFastAuthSessionNotFoundException()
    {
        var handler = CreateHandler();
        var act = () => handler.Handle(
            new RejectFastAuthCommand { FastAuthId = "nonexistent", ConfirmationCode = "code" },
            CancellationToken.None);

        await act.Should().ThrowAsync<FastAuthSessionNotFoundException>();
    }

    [Fact]
    public async Task Handle_NonexistentSession_DoesNotIncrementMetric()
    {
        var handler = CreateHandler();
        try
        {
            await handler.Handle(
                new RejectFastAuthCommand { FastAuthId = "nonexistent", ConfirmationCode = "code" },
                CancellationToken.None);
        }
        catch (FastAuthSessionNotFoundException) { }

        var snapshot = _h.Metrics.SnapshotAndReset();
        snapshot.Should().NotContainKey("sessions_rejected");
    }

    #endregion

    #region Expired Session

    [Fact]
    public async Task Handle_ExpiredSession_ThrowsFastAuthSessionExpiredException()
    {
        var session = _h.SessionsManager.Create("D", "OS", "A", "V", "IP");
        session.TryScan(42);
        session.TryExpire();
        var handler = CreateHandler();

        var act = () => handler.Handle(
            new RejectFastAuthCommand { FastAuthId = session.Id, ConfirmationCode = "code" },
            CancellationToken.None);

        await act.Should().ThrowAsync<FastAuthSessionExpiredException>();
    }

    #endregion

    #region Invalid State

    [Fact]
    public async Task Handle_PendingSession_ThrowsFastAuthInvalidStateException()
    {
        var session = _h.SessionsManager.Create("D", "OS", "A", "V", "IP");
        var handler = CreateHandler();

        var act = () => handler.Handle(
            new RejectFastAuthCommand { FastAuthId = session.Id, ConfirmationCode = "code" },
            CancellationToken.None);

        await act.Should().ThrowAsync<FastAuthInvalidStateException>();
    }

    [Fact]
    public async Task Handle_AlreadyAcceptedSession_ThrowsFastAuthInvalidStateException()
    {
        var (session, code) = CreateAndScanSession();
        session.TryAccept(code, 42, new FastAuthResult
        {
            Status = FastAuthStatus.Accepted,
            AccessToken = "token"
        });
        var handler = CreateHandler();

        var act = () => handler.Handle(
            new RejectFastAuthCommand { FastAuthId = session.Id, ConfirmationCode = code },
            CancellationToken.None);

        await act.Should().ThrowAsync<FastAuthInvalidStateException>();
    }

    [Fact]
    public async Task Handle_AlreadyRejectedSession_ThrowsFastAuthInvalidStateException()
    {
        var (session, code) = CreateAndScanSession();
        var handler = CreateHandler();

        await handler.Handle(
            new RejectFastAuthCommand { FastAuthId = session.Id, ConfirmationCode = code },
            CancellationToken.None);

        var act = () => handler.Handle(
            new RejectFastAuthCommand { FastAuthId = session.Id, ConfirmationCode = code },
            CancellationToken.None);

        await act.Should().ThrowAsync<FastAuthInvalidStateException>();
    }

    #endregion

    #region Invalid Confirmation Code

    [Fact]
    public async Task Handle_WrongConfirmationCode_ThrowsFastAuthInvalidConfirmationCodeException()
    {
        var (session, _) = CreateAndScanSession();
        var handler = CreateHandler();

        var act = () => handler.Handle(
            new RejectFastAuthCommand { FastAuthId = session.Id, ConfirmationCode = "wrong-code" },
            CancellationToken.None);

        await act.Should().ThrowAsync<FastAuthInvalidConfirmationCodeException>();
    }

    [Fact]
    public async Task Handle_EmptyConfirmationCode_ThrowsFastAuthInvalidConfirmationCodeException()
    {
        var (session, _) = CreateAndScanSession();
        var handler = CreateHandler();

        var act = () => handler.Handle(
            new RejectFastAuthCommand { FastAuthId = session.Id, ConfirmationCode = "" },
            CancellationToken.None);

        await act.Should().ThrowAsync<FastAuthInvalidConfirmationCodeException>();
    }

    #endregion

    #region Wrong User

    [Fact]
    public async Task Handle_WrongUserId_ThrowsFastAuthInvalidConfirmationCodeException()
    {
        var (session, code) = CreateAndScanSession(userId: 42);
        var handler = CreateHandler(userId: 99);

        var act = () => handler.Handle(
            new RejectFastAuthCommand { FastAuthId = session.Id, ConfirmationCode = code },
            CancellationToken.None);

        await act.Should().ThrowAsync<FastAuthInvalidConfirmationCodeException>();
    }

    #endregion
}
