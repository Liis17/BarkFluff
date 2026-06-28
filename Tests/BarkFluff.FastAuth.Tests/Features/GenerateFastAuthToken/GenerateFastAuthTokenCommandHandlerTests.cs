using BarkFluff.FastAuth.Features.GenerateFastAuthToken;
using BarkFluff.FastAuth.Infrastructure;
using BarkFluff.GrpcServer.Tracker;
using BarkFluff.Proto.FastAuth;
using BarkFluff.Shared.Exceptions.Identity;

namespace BarkFluff.FastAuth.Tests.Features.GenerateFastAuthToken;

public class GenerateFastAuthTokenCommandHandlerTests
{
    private readonly TestHelper _h = new();

    private GenerateFastAuthTokenCommandHandler CreateHandler(
        RequestContext? requestContext = null,
        FastAuthSessionsManager? sessions = null,
        QrCodeGenerator? qr = null)
    {
        return new GenerateFastAuthTokenCommandHandler(
            sessions ?? _h.SessionsManager,
            qr ?? new QrCodeGenerator(),
            requestContext ?? _h.CreateRequestContext(),
            _h.Metrics,
            TestHelper.CreateLogger<GenerateFastAuthTokenCommandHandler>());
    }

    #region Success

    [Fact]
    public async Task Handle_ValidRequest_ReturnsResponse()
    {
        var handler = CreateHandler();
        var result = await handler.Handle(
            new GenerateFastAuthTokenCommand { Format = TokenFormat.Qr },
            CancellationToken.None);

        result.Should().NotBeNull();
    }

    [Fact]
    public async Task Handle_ValidRequest_ReturnsFastAuthId()
    {
        var handler = CreateHandler();
        var result = await handler.Handle(
            new GenerateFastAuthTokenCommand { Format = TokenFormat.Qr },
            CancellationToken.None);

        result.FastAuthId.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Handle_ValidRequest_ReturnsFutureExpiresAt()
    {
        var handler = CreateHandler();
        var before = DateTime.UtcNow;
        var result = await handler.Handle(
            new GenerateFastAuthTokenCommand { Format = TokenFormat.Qr },
            CancellationToken.None);

        result.ExpiresAt.Should().NotBeNull();
        result.ExpiresAt.ToDateTime().Should().BeAfter(before);
    }

    [Fact]
    public async Task Handle_ValidRequest_ReturnsTokenWithValue()
    {
        var handler = CreateHandler();
        var result = await handler.Handle(
            new GenerateFastAuthTokenCommand { Format = TokenFormat.Qr },
            CancellationToken.None);

        result.Token.Should().NotBeNull();
        result.Token.Value.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Handle_QrFormat_ReturnsBase64QrCode()
    {
        var handler = CreateHandler();
        var result = await handler.Handle(
            new GenerateFastAuthTokenCommand { Format = TokenFormat.Qr },
            CancellationToken.None);

        result.Token.Format.Should().Be(TokenFormat.Qr);
        var act = () => Convert.FromBase64String(result.Token.Value);
        act.Should().NotThrow();
    }

    [Fact]
    public async Task Handle_RawFormat_ReturnsSessionId()
    {
        var handler = CreateHandler();
        var result = await handler.Handle(
            new GenerateFastAuthTokenCommand { Format = TokenFormat.Text },
            CancellationToken.None);

        result.Token.Format.Should().Be(TokenFormat.Text);
        result.Token.Value.Should().Be(result.FastAuthId);
    }

    [Fact]
    public async Task Handle_UnknownFormat_DefaultsToQr()
    {
        var handler = CreateHandler();
        var result = await handler.Handle(
            new GenerateFastAuthTokenCommand { Format = TokenFormat.Unknown },
            CancellationToken.None);

        result.Token.Format.Should().Be(TokenFormat.Qr);
    }

    [Fact]
    public async Task Handle_ValidRequest_CreatesSessionInManager()
    {
        var handler = CreateHandler();
        var result = await handler.Handle(
            new GenerateFastAuthTokenCommand { Format = TokenFormat.Text },
            CancellationToken.None);

        _h.SessionsManager.TryGet(result.FastAuthId).Should().NotBeNull();
    }

    [Fact]
    public async Task Handle_ValidRequest_IncrementsSessionsGeneratedMetric()
    {
        var handler = CreateHandler();
        await handler.Handle(
            new GenerateFastAuthTokenCommand { Format = TokenFormat.Qr },
            CancellationToken.None);

        var snapshot = _h.Metrics.SnapshotAndReset();
        snapshot.Should().ContainKey("sessions_generated");
        snapshot["sessions_generated"].Should().Be(1);
    }

    [Fact]
    public async Task Handle_ValidRequest_UsesDeviceMetadataFromContext()
    {
        var ctx = _h.CreateRequestContext("MyPhone", "iOS", "BF", "3.0", "192.168.1.1");
        var handler = CreateHandler(requestContext: ctx);
        var result = await handler.Handle(
            new GenerateFastAuthTokenCommand { Format = TokenFormat.Text },
            CancellationToken.None);

        var session = _h.SessionsManager.TryGet(result.FastAuthId);
        session.Should().NotBeNull();
        session!.DeviceName.Should().Be("MyPhone");
        session.OperationSystem.Should().Be("iOS");
        session.AppName.Should().Be("BF");
        session.AppVersion.Should().Be("3.0");
        session.IpAddress.Should().Be("192.168.1.1");
    }

    [Fact]
    public async Task Handle_NullIpAddress_UsesEmptyString()
    {
        var ctx = _h.CreateRequestContext(ipAddress: null);
        var handler = CreateHandler(requestContext: ctx);
        var result = await handler.Handle(
            new GenerateFastAuthTokenCommand { Format = TokenFormat.Text },
            CancellationToken.None);

        var session = _h.SessionsManager.TryGet(result.FastAuthId);
        session!.IpAddress.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_MultipleCalls_CreateDistinctSessions()
    {
        var handler = CreateHandler();
        var r1 = await handler.Handle(new GenerateFastAuthTokenCommand { Format = TokenFormat.Text }, CancellationToken.None);
        var r2 = await handler.Handle(new GenerateFastAuthTokenCommand { Format = TokenFormat.Text }, CancellationToken.None);

        r1.FastAuthId.Should().NotBe(r2.FastAuthId);
    }

    #endregion

    #region Validation

    [Fact]
    public async Task Handle_NullDeviceName_ThrowsXDeviceNameIsRequiredException()
    {
        var ctx = _h.CreateRequestContext(deviceName: null);
        var handler = CreateHandler(requestContext: ctx);
        var act = () => handler.Handle(
            new GenerateFastAuthTokenCommand { Format = TokenFormat.Qr },
            CancellationToken.None);

        await act.Should().ThrowAsync<XDeviceNameIsRequiredException>();
    }

    [Fact]
    public async Task Handle_EmptyDeviceName_ThrowsXDeviceNameIsRequiredException()
    {
        var ctx = _h.CreateRequestContext(deviceName: "");
        var handler = CreateHandler(requestContext: ctx);
        var act = () => handler.Handle(
            new GenerateFastAuthTokenCommand { Format = TokenFormat.Qr },
            CancellationToken.None);

        await act.Should().ThrowAsync<XDeviceNameIsRequiredException>();
    }

    [Fact]
    public async Task Handle_NullOs_ThrowsXOsNameIsRequiredException()
    {
        var ctx = _h.CreateRequestContext(os: null);
        var handler = CreateHandler(requestContext: ctx);
        var act = () => handler.Handle(
            new GenerateFastAuthTokenCommand { Format = TokenFormat.Qr },
            CancellationToken.None);

        await act.Should().ThrowAsync<XOsNameIsRequiredException>();
    }

    [Fact]
    public async Task Handle_EmptyOs_ThrowsXOsNameIsRequiredException()
    {
        var ctx = _h.CreateRequestContext(os: "");
        var handler = CreateHandler(requestContext: ctx);
        var act = () => handler.Handle(
            new GenerateFastAuthTokenCommand { Format = TokenFormat.Qr },
            CancellationToken.None);

        await act.Should().ThrowAsync<XOsNameIsRequiredException>();
    }

    [Fact]
    public async Task Handle_NullAppName_ThrowsXAppInfoIsRequiedException()
    {
        var ctx = _h.CreateRequestContext(appName: null);
        var handler = CreateHandler(requestContext: ctx);
        var act = () => handler.Handle(
            new GenerateFastAuthTokenCommand { Format = TokenFormat.Qr },
            CancellationToken.None);

        await act.Should().ThrowAsync<XAppInfoIsRequiedException>();
    }

    [Fact]
    public async Task Handle_EmptyAppName_ThrowsXAppInfoIsRequiedException()
    {
        var ctx = _h.CreateRequestContext(appName: "");
        var handler = CreateHandler(requestContext: ctx);
        var act = () => handler.Handle(
            new GenerateFastAuthTokenCommand { Format = TokenFormat.Qr },
            CancellationToken.None);

        await act.Should().ThrowAsync<XAppInfoIsRequiedException>();
    }

    [Fact]
    public async Task Handle_NullAppVersion_ThrowsXAppInfoIsRequiedException()
    {
        var ctx = _h.CreateRequestContext(appVersion: null);
        var handler = CreateHandler(requestContext: ctx);
        var act = () => handler.Handle(
            new GenerateFastAuthTokenCommand { Format = TokenFormat.Qr },
            CancellationToken.None);

        await act.Should().ThrowAsync<XAppInfoIsRequiedException>();
    }

    [Fact]
    public async Task Handle_EmptyAppVersion_ThrowsXAppInfoIsRequiedException()
    {
        var ctx = _h.CreateRequestContext(appVersion: "");
        var handler = CreateHandler(requestContext: ctx);
        var act = () => handler.Handle(
            new GenerateFastAuthTokenCommand { Format = TokenFormat.Qr },
            CancellationToken.None);

        await act.Should().ThrowAsync<XAppInfoIsRequiedException>();
    }

    [Fact]
    public async Task Handle_ValidationFailure_DoesNotCreateSession()
    {
        var ctx = _h.CreateRequestContext(deviceName: null);
        var handler = CreateHandler(requestContext: ctx);
        try
        {
            await handler.Handle(
                new GenerateFastAuthTokenCommand { Format = TokenFormat.Qr },
                CancellationToken.None);
        }
        catch (XDeviceNameIsRequiredException) { }

        _h.SessionsManager.Snapshot().Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_ValidationFailure_DoesNotIncrementMetric()
    {
        var ctx = _h.CreateRequestContext(deviceName: null);
        var handler = CreateHandler(requestContext: ctx);
        try
        {
            await handler.Handle(
                new GenerateFastAuthTokenCommand { Format = TokenFormat.Qr },
                CancellationToken.None);
        }
        catch (XDeviceNameIsRequiredException) { }

        var snapshot = _h.Metrics.SnapshotAndReset();
        snapshot.Should().NotContainKey("sessions_generated");
    }

    #endregion
}
