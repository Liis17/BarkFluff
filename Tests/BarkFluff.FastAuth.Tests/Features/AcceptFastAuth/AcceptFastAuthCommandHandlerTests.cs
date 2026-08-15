using BarkFluff.FastAuth.Features.AcceptFastAuth;
using BarkFluff.Proto.FastAuth;
using BarkFluff.Proto.Identity;
using BarkFluff.Shared.Exceptions.FastAuth;

namespace BarkFluff.FastAuth.Tests.Features.AcceptFastAuth;

public class AcceptFastAuthCommandHandlerTests
{
    private readonly TestHelper _h = new();
    private readonly Mock<IdentityServerApi.IdentityServerApiClient> _identityMock;

    public AcceptFastAuthCommandHandlerTests()
    {
        _identityMock = _h.CreateIdentityClientMock();
        _h.SetupIdentityClientSuccess(_identityMock);
    }

    private AcceptFastAuthCommandHandler CreateHandler(long userId = 42)
    {
        return new AcceptFastAuthCommandHandler(
            _h.Store,
            _h.EventBus,
            _identityMock.Object,
            _h.CreateUserContext(userId),
            _h.Metrics,
            TestHelper.CreateLogger<AcceptFastAuthCommandHandler>());
    }

    #region Success

    [Fact]
    public async Task Handle_ValidRequest_ReturnsResponse()
    {
        var (session, code) = await _h.CreateAndScanSessionAsync();
        var handler = CreateHandler();

        var result = await handler.Handle(
            new AcceptFastAuthCommand { FastAuthId = session.Id, ConfirmationCode = code },
            CancellationToken.None);

        result.Should().NotBeNull();
    }

    [Fact]
    public async Task Handle_ValidRequest_SetsSessionStatusToAccepted()
    {
        var (session, code) = await _h.CreateAndScanSessionAsync();
        var handler = CreateHandler();

        await handler.Handle(
            new AcceptFastAuthCommand { FastAuthId = session.Id, ConfirmationCode = code },
            CancellationToken.None);

        var stored = await _h.Store.GetAsync(session.Id);
        stored!.Status.Should().Be(FastAuthStatus.Accepted);
    }

    [Fact]
    public async Task Handle_ValidRequest_CallsIdentityService()
    {
        var (session, code) = await _h.CreateAndScanSessionAsync();
        var handler = CreateHandler();

        await handler.Handle(
            new AcceptFastAuthCommand { FastAuthId = session.Id, ConfirmationCode = code },
            CancellationToken.None);

        _identityMock.Verify(
            c => c.CreateSessionForUserServerAsync(
                It.Is<CreateSessionForUserServerRequest>(r =>
                    r.UserId == 42 &&
                    !string.IsNullOrEmpty(r.DeviceId) &&
                    r.DeviceName == "TestDevice" &&
                    r.OperationSystem == "Windows" &&
                    r.IpAddress == "127.0.0.1"),
                null, null, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_ValidRequest_PublishesAcceptedEventWithTokens()
    {
        var (session, code) = await _h.CreateAndScanSessionAsync();
        var handler = CreateHandler();

        await handler.Handle(
            new AcceptFastAuthCommand { FastAuthId = session.Id, ConfirmationCode = code },
            CancellationToken.None);

        var stored = await _h.Store.GetAsync(session.Id);
        stored!.Status.Should().Be(FastAuthStatus.Accepted);
        stored.Result.Should().NotBeNull();
        stored.Result!.Status.Should().Be(FastAuthStatus.Accepted);
        stored.Result.AccessToken.Should().Be("access_token");
        stored.Result.RefreshToken.Should().Be("refresh_token");
    }

    [Fact]
    public async Task Handle_ValidRequest_IncrementsSessionsAcceptedMetric()
    {
        var (session, code) = await _h.CreateAndScanSessionAsync();
        var handler = CreateHandler();

        await handler.Handle(
            new AcceptFastAuthCommand { FastAuthId = session.Id, ConfirmationCode = code },
            CancellationToken.None);

        var snapshot = _h.Metrics.SnapshotAndReset();
        snapshot.Should().ContainKey("sessions_accepted");
        snapshot["sessions_accepted"].Should().Be(1);
    }

    #endregion

    #region Session Not Found

    [Fact]
    public async Task Handle_NonexistentSession_ThrowsFastAuthSessionNotFoundException()
    {
        var handler = CreateHandler();
        var act = () => handler.Handle(
            new AcceptFastAuthCommand { FastAuthId = "nonexistent", ConfirmationCode = "code" },
            CancellationToken.None);

        await act.Should().ThrowAsync<FastAuthSessionNotFoundException>();
    }

    [Fact]
    public async Task Handle_NonexistentSession_DoesNotCallIdentityService()
    {
        var handler = CreateHandler();
        try
        {
            await handler.Handle(
                new AcceptFastAuthCommand { FastAuthId = "nonexistent", ConfirmationCode = "code" },
                CancellationToken.None);
        }
        catch (FastAuthSessionNotFoundException) { }

        _identityMock.Verify(
            c => c.CreateSessionForUserServerAsync(
                It.IsAny<CreateSessionForUserServerRequest>(),
                null, null, It.IsAny<CancellationToken>()),
            Times.Never);
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
            new AcceptFastAuthCommand { FastAuthId = session.Id, ConfirmationCode = code },
            CancellationToken.None);

        await act.Should().ThrowAsync<FastAuthSessionExpiredException>();
    }

    [Fact]
    public async Task Handle_SessionExpiredByWallClock_ThrowsFastAuthSessionExpiredException()
    {
        var session = _h.CreateSession(expiresAt: DateTime.UtcNow - TimeSpan.FromSeconds(1));
        var handler = CreateHandler();

        var act = () => handler.Handle(
            new AcceptFastAuthCommand { FastAuthId = session.Id, ConfirmationCode = "code" },
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
            new AcceptFastAuthCommand { FastAuthId = session.Id, ConfirmationCode = "code" },
            CancellationToken.None);

        await act.Should().ThrowAsync<FastAuthInvalidStateException>();
    }

    [Fact]
    public async Task Handle_AlreadyAcceptedSession_ThrowsFastAuthInvalidStateException()
    {
        var (session, code) = await _h.CreateAndScanSessionAsync();
        var handler = CreateHandler();

        await handler.Handle(
            new AcceptFastAuthCommand { FastAuthId = session.Id, ConfirmationCode = code },
            CancellationToken.None);

        var act = () => handler.Handle(
            new AcceptFastAuthCommand { FastAuthId = session.Id, ConfirmationCode = code },
            CancellationToken.None);

        await act.Should().ThrowAsync<FastAuthInvalidStateException>();
    }

    [Fact]
    public async Task Handle_RejectedSession_ThrowsFastAuthInvalidStateException()
    {
        var (session, code) = await _h.CreateAndScanSessionAsync();
        await _h.Store.TryRejectAsync(session.Id, code, 42);
        var handler = CreateHandler();

        var act = () => handler.Handle(
            new AcceptFastAuthCommand { FastAuthId = session.Id, ConfirmationCode = code },
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
            new AcceptFastAuthCommand { FastAuthId = session.Id, ConfirmationCode = "wrong-code" },
            CancellationToken.None);

        await act.Should().ThrowAsync<FastAuthInvalidConfirmationCodeException>();
    }

    [Fact]
    public async Task Handle_WrongConfirmationCode_DoesNotCallIdentityService()
    {
        var (session, _) = await _h.CreateAndScanSessionAsync();
        var handler = CreateHandler();

        try
        {
            await handler.Handle(
                new AcceptFastAuthCommand { FastAuthId = session.Id, ConfirmationCode = "wrong-code" },
                CancellationToken.None);
        }
        catch (FastAuthInvalidConfirmationCodeException) { }

        _identityMock.Verify(
            c => c.CreateSessionForUserServerAsync(
                It.IsAny<CreateSessionForUserServerRequest>(),
                null, null, It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_EmptyConfirmationCode_ThrowsFastAuthInvalidConfirmationCodeException()
    {
        var (session, _) = await _h.CreateAndScanSessionAsync();
        var handler = CreateHandler();

        var act = () => handler.Handle(
            new AcceptFastAuthCommand { FastAuthId = session.Id, ConfirmationCode = "" },
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
            new AcceptFastAuthCommand { FastAuthId = session.Id, ConfirmationCode = code },
            CancellationToken.None);

        await act.Should().ThrowAsync<FastAuthInvalidConfirmationCodeException>();
    }

    [Fact]
    public async Task Handle_WrongUserId_DoesNotCallIdentityService()
    {
        var (session, code) = await _h.CreateAndScanSessionAsync(userId: 42);
        var handler = CreateHandler(userId: 99);

        try
        {
            await handler.Handle(
                new AcceptFastAuthCommand { FastAuthId = session.Id, ConfirmationCode = code },
                CancellationToken.None);
        }
        catch (FastAuthInvalidConfirmationCodeException) { }

        _identityMock.Verify(
            c => c.CreateSessionForUserServerAsync(
                It.IsAny<CreateSessionForUserServerRequest>(),
                null, null, It.IsAny<CancellationToken>()),
            Times.Never);
    }

    #endregion
}
