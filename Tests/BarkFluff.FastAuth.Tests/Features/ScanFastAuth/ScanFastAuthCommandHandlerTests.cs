using BarkFluff.FastAuth.Features.ScanFastAuth;
using BarkFluff.FastAuth.Infrastructure;
using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Proto.FastAuth;
using BarkFluff.Shared.Exceptions.FastAuth;

namespace BarkFluff.FastAuth.Tests.Features.ScanFastAuth;

public class ScanFastAuthCommandHandlerTests
{
    private readonly TestHelper _h = new();

    private ScanFastAuthCommandHandler CreateHandler(long userId = 42)
    {
        return new ScanFastAuthCommandHandler(
            _h.SessionsManager,
            _h.CreateUserContext(userId),
            _h.Metrics,
            TestHelper.CreateLogger<ScanFastAuthCommandHandler>());
    }

    #region Success

    [Fact]
    public async Task Handle_ValidRequest_ReturnsResponse()
    {
        var session = _h.SessionsManager.Create("MyPhone", "Android", "BF", "2.0", "10.0.0.1");
        var handler = CreateHandler();

        var result = await handler.Handle(
            new ScanFastAuthCommand { FastAuthId = session.Id },
            CancellationToken.None);

        result.Should().NotBeNull();
    }

    [Fact]
    public async Task Handle_ValidRequest_ReturnsDeviceMetadata()
    {
        var session = _h.SessionsManager.Create("MyPhone", "Android", "BF", "2.0", "10.0.0.1");
        var handler = CreateHandler();

        var result = await handler.Handle(
            new ScanFastAuthCommand { FastAuthId = session.Id },
            CancellationToken.None);

        result.DeviceName.Should().Be("MyPhone");
        result.OperationSystem.Should().Be("Android");
        result.AppName.Should().Be("BF");
        result.AppVersion.Should().Be("2.0");
        result.IpAddress.Should().Be("10.0.0.1");
    }

    [Fact]
    public async Task Handle_ValidRequest_ReturnsConfirmationCode()
    {
        var session = _h.SessionsManager.Create("D", "OS", "A", "V", "IP");
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
        var session = _h.SessionsManager.Create("D", "OS", "A", "V", "IP");
        var handler = CreateHandler();

        var result = await handler.Handle(
            new ScanFastAuthCommand { FastAuthId = session.Id },
            CancellationToken.None);

        result.ExpiresAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Handle_ValidRequest_SetsSessionStatusToScanned()
    {
        var session = _h.SessionsManager.Create("D", "OS", "A", "V", "IP");
        var handler = CreateHandler();

        await handler.Handle(
            new ScanFastAuthCommand { FastAuthId = session.Id },
            CancellationToken.None);

        session.Status.Should().Be(FastAuthStatus.Scanned);
    }

    [Fact]
    public async Task Handle_ValidRequest_SetsUserIdOnSession()
    {
        var session = _h.SessionsManager.Create("D", "OS", "A", "V", "IP");
        var handler = CreateHandler(userId: 42);

        await handler.Handle(
            new ScanFastAuthCommand { FastAuthId = session.Id },
            CancellationToken.None);

        session.UserId.Should().Be(42);
    }

    [Fact]
    public async Task Handle_ValidRequest_IncrementsSessionsScannedMetric()
    {
        var session = _h.SessionsManager.Create("D", "OS", "A", "V", "IP");
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
        var session = _h.SessionsManager.Create("D", "OS", "A", "V", "IP");
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
        var session = _h.SessionsManager.Create("D", "OS", "A", "V", "IP");
        session.TryExpire();
        var handler = CreateHandler();

        var act = () => handler.Handle(
            new ScanFastAuthCommand { FastAuthId = session.Id },
            CancellationToken.None);

        await act.Should().ThrowAsync<FastAuthInvalidStateException>();
    }

    #endregion
}
