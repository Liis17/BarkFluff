using BarkFluff.FastAuth.Features.RejectFastAuth;
using BarkFluff.Proto.FastAuth;
using BarkFluff.Shared.Exceptions.FastAuth;

namespace BarkFluff.FastAuth.Tests.Features.RejectFastAuth;

public class RejectFastAuthCommandHandlerTests
{
    private readonly TestHelper _h = new();

    private RejectFastAuthCommandHandler CreateHandler(long userId = 42)
    {
        return new RejectFastAuthCommandHandler(
            _h.Store,
            _h.EventBus,
            _h.CreateUserContext(userId),
            _h.Metrics,
            TestHelper.CreateLogger<RejectFastAuthCommandHandler>());
    }

    #region Success

    [Fact]
    public async Task Handle_ValidRequest_ReturnsResponse()
    {
        var (session, code) = await _h.CreateAndScanSessionAsync();
        var handler = CreateHandler();

        var result = await handler.Handle(
            new RejectFastAuthCommand { FastAuthId = session.Id, ConfirmationCode = code },
            CancellationToken.None);

        result.Should().NotBeNull();
    }

    [Fact]
    public async Task Handle_ValidRequest_SetsSessionStatusToRejected()
    {
        var (session, code) = await _h.CreateAndScanSessionAsync();
        var handler = CreateHandler();

        await handler.Handle(
            new RejectFastAuthCommand { FastAuthId = session.Id, ConfirmationCode = code },
            CancellationToken.None);

        var stored = await _h.Store.GetAsync(session.Id);
        stored!.Status.Should().Be(FastAuthStatus.Rejected);
    }

    [Fact]
    public async Task Handle_ValidRequest_MakesSessionFinal()
    {
        var (session, code) = await _h.CreateAndScanSessionAsync();
        var handler = CreateHandler();

        await handler.Handle(
            new RejectFastAuthCommand { FastAuthId = session.Id, ConfirmationCode = code },
            CancellationToken.None);

        var stored = await _h.Store.GetAsync(session.Id);
        stored!.IsFinal.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_ValidRequest_PublishesRejectedEvent()
    {
        var (session, code) = await _h.CreateAndScanSessionAsync();
        var handler = CreateHandler();

        var reader = _h.EventBus.Attach(session.Id);
        await handler.Handle(
            new RejectFastAuthCommand { FastAuthId = session.Id, ConfirmationCode = code },
            CancellationToken.None);

        reader.TryRead(out var evt).Should().BeTrue();
        evt.Status.Should().Be(FastAuthStatus.Rejected);
    }

    [Fact]
    public async Task Handle_ValidRequest_IncrementsSessionsRejectedMetric()
    {
        var (session, code) = await _h.CreateAndScanSessionAsync();
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

    #endregion

    #region Expired Session

    [Fact]
    public async Task Handle_ExpiredSession_ThrowsFastAuthSessionExpiredException()
    {
        var (session, code) = await _h.CreateAndScanSessionAsync();
        await _h.Store.TryExpireAsync(session.Id);
        var handler = CreateHandler();

        var act = () => handler.Handle(
            new RejectFastAuthCommand { FastAuthId = session.Id, ConfirmationCode = code },
            CancellationToken.None);

        await act.Should().ThrowAsync<FastAuthSessionExpiredException>();
    }

    [Fact]
    public async Task Handle_SessionExpiredByWallClock_ThrowsFastAuthSessionExpiredException()
    {
        var session = _h.CreateSession(expiresAt: DateTime.UtcNow - TimeSpan.FromSeconds(1));
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
        var session = _h.CreateSession();
        var handler = CreateHandler();

        var act = () => handler.Handle(
            new RejectFastAuthCommand { FastAuthId = session.Id, ConfirmationCode = "code" },
            CancellationToken.None);

        await act.Should().ThrowAsync<FastAuthInvalidStateException>();
    }

    [Fact]
    public async Task Handle_AlreadyRejectedSession_ThrowsFastAuthInvalidStateException()
    {
        var (session, code) = await _h.CreateAndScanSessionAsync();
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
        var (session, _) = await _h.CreateAndScanSessionAsync();
        var handler = CreateHandler();

        var act = () => handler.Handle(
            new RejectFastAuthCommand { FastAuthId = session.Id, ConfirmationCode = "wrong-code" },
            CancellationToken.None);

        await act.Should().ThrowAsync<FastAuthInvalidConfirmationCodeException>();
    }

    #endregion

    #region Wrong User

    [Fact]
    public async Task Handle_WrongUserId_ThrowsFastAuthInvalidConfirmationCodeException()
    {
        var (session, code) = await _h.CreateAndScanSessionAsync(userId: 42);
        var handler = CreateHandler(userId: 99);

        var act = () => handler.Handle(
            new RejectFastAuthCommand { FastAuthId = session.Id, ConfirmationCode = code },
            CancellationToken.None);

        await act.Should().ThrowAsync<FastAuthInvalidConfirmationCodeException>();
    }

    #endregion
}
