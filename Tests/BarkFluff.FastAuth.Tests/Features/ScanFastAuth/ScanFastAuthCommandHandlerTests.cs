using BarkFluff.FastAuth.Features.ScanFastAuth;
using BarkFluff.Proto.FastAuth;
using BarkFluff.Shared.Exceptions.FastAuth;

namespace BarkFluff.FastAuth.Tests.Features.ScanFastAuth;

public class ScanFastAuthCommandHandlerTests
{
    private readonly TestHelper _h = new();

    private ScanFastAuthCommandHandler CreateHandler(long userId = 42)
    {
        return new ScanFastAuthCommandHandler(
            _h.Store,
            _h.EventBus,
            _h.CreateUserContext(userId),
            _h.Metrics,
            TestHelper.CreateLogger<ScanFastAuthCommandHandler>());
    }

    #region Success

    [Fact]
    public async Task Handle_ValidRequest_ReturnsResponse()
    {
        var session = _h.CreateSession();
        var handler = CreateHandler();

        var result = await handler.Handle(
            new ScanFastAuthCommand { FastAuthId = session.Id },
            CancellationToken.None);

        result.Should().NotBeNull();
    }

    [Fact]
    public async Task Handle_ValidRequest_ReturnsDeviceMetadata()
    {
        var session = _h.CreateSession();
        var handler = CreateHandler();

        var result = await handler.Handle(
            new ScanFastAuthCommand { FastAuthId = session.Id },
            CancellationToken.None);

        result.DeviceName.Should().Be("TestDevice");
        result.OperationSystem.Should().Be("Windows");
        result.AppName.Should().Be("BarkFluff");
        result.AppVersion.Should().Be("1.0");
        result.IpAddress.Should().Be("127.0.0.1");
    }

    [Fact]
    public async Task Handle_ValidRequest_ReturnsConfirmationCode()
    {
        var session = _h.CreateSession();
        var handler = CreateHandler();

        var result = await handler.Handle(
            new ScanFastAuthCommand { FastAuthId = session.Id },
            CancellationToken.None);

        result.ConfirmationCode.Should().NotBeNullOrEmpty();
        Guid.TryParse(result.ConfirmationCode, out _).Should().BeTrue();
    }

    [Fact]
    public async Task Handle_ValidRequest_ReturnsExpiresAt()
    {
        var session = _h.CreateSession();
        var handler = CreateHandler();

        var result = await handler.Handle(
            new ScanFastAuthCommand { FastAuthId = session.Id },
            CancellationToken.None);

        result.ExpiresAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Handle_ValidRequest_SetsSessionStatusToScanned()
    {
        var session = _h.CreateSession();
        var handler = CreateHandler();

        await handler.Handle(
            new ScanFastAuthCommand { FastAuthId = session.Id },
            CancellationToken.None);

        var stored = await _h.Store.GetAsync(session.Id);
        stored!.Status.Should().Be(FastAuthStatus.Scanned);
    }

    [Fact]
    public async Task Handle_ValidRequest_SetsUserIdOnSession()
    {
        var session = _h.CreateSession();
        var handler = CreateHandler(userId: 42);

        await handler.Handle(
            new ScanFastAuthCommand { FastAuthId = session.Id },
            CancellationToken.None);

        var stored = await _h.Store.GetAsync(session.Id);
        stored!.UserId.Should().Be(42);
    }

    [Fact]
    public async Task Handle_ValidRequest_PublishesScannedEvent()
    {
        var session = _h.CreateSession();
        var handler = CreateHandler();

        var reader = _h.EventBus.Attach(session.Id);
        await handler.Handle(
            new ScanFastAuthCommand { FastAuthId = session.Id },
            CancellationToken.None);

        reader.TryRead(out var evt).Should().BeTrue();
        evt.Status.Should().Be(FastAuthStatus.Scanned);
    }

    [Fact]
    public async Task Handle_ValidRequest_IncrementsSessionsScannedMetric()
    {
        var session = _h.CreateSession();
        var handler = CreateHandler();

        await handler.Handle(
            new ScanFastAuthCommand { FastAuthId = session.Id },
            CancellationToken.None);

        var snapshot = _h.Metrics.SnapshotAndReset();
        snapshot.Should().ContainKey("sessions_scanned");
        snapshot["sessions_scanned"].Should().Be(1);
    }

    #endregion

    #region Session Not Found

    [Fact]
    public async Task Handle_NonexistentSession_ThrowsFastAuthSessionNotFoundException()
    {
        var handler = CreateHandler();
        var act = () => handler.Handle(
            new ScanFastAuthCommand { FastAuthId = "nonexistent" },
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
                new ScanFastAuthCommand { FastAuthId = "nonexistent" },
                CancellationToken.None);
        }
        catch (FastAuthSessionNotFoundException) { }

        var snapshot = _h.Metrics.SnapshotAndReset();
        snapshot.Should().NotContainKey("sessions_scanned");
    }

    #endregion

    #region Already Scanned

    [Fact]
    public async Task Handle_AlreadyScannedSession_ThrowsFastAuthInvalidStateException()
    {
        var session = _h.CreateSession();
        var handler = CreateHandler();

        await handler.Handle(
            new ScanFastAuthCommand { FastAuthId = session.Id },
            CancellationToken.None);

        var act = () => handler.Handle(
            new ScanFastAuthCommand { FastAuthId = session.Id },
            CancellationToken.None);

        await act.Should().ThrowAsync<FastAuthInvalidStateException>();
    }

    #endregion

    #region Expired Session

    [Fact]
    public async Task Handle_ExpiredSession_ThrowsFastAuthSessionExpiredException()
    {
        var session = _h.CreateSession(expiresAt: DateTime.UtcNow - TimeSpan.FromSeconds(1));
        var handler = CreateHandler();

        var act = () => handler.Handle(
            new ScanFastAuthCommand { FastAuthId = session.Id },
            CancellationToken.None);

        await act.Should().ThrowAsync<FastAuthSessionExpiredException>();
    }

    [Fact]
    public async Task Handle_AlreadyFinalSession_ThrowsFastAuthInvalidStateException()
    {
        var session = _h.CreateSession();
        await _h.Store.TryExpireAsync(session.Id);
        var handler = CreateHandler();

        var act = () => handler.Handle(
            new ScanFastAuthCommand { FastAuthId = session.Id },
            CancellationToken.None);

        await act.Should().ThrowAsync<FastAuthInvalidStateException>();
    }

    #endregion
}
