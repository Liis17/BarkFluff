using BarkFluff.GrpcServer.Metrics;
using BarkFluff.GrpcServer.Tracker;
using BarkFluff.Identity.Domain;
using BarkFluff.Identity.Features.ResetPassword;
using BarkFluff.Identity.Infrastructure;
using BarkFluff.Identity.Persistence.Contexts;
using BarkFluff.Identity.Persistence.Services;
using BarkFluff.Identity.Services;
using BarkFluff.Proto.Users;
using BarkFluff.Shared.Exceptions.Identity;

using Grpc.Core;

using MassTransit;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Moq;

using Xunit;

using User = BarkFluff.Proto.Users.User;

namespace BarkFluff.Identity.Tests.Features;

public class ResetPasswordCommandHandlerTests
{
    private readonly ResetPasswordsStorage _resetPasswordsStorage;
    private readonly AuthPropertiesStorage _authPropsStorage;
    private readonly Mock<UsersServerApi.UsersServerApiClient> _usersClient;
    private readonly RequestContext _requestContext;
    private readonly Mock<IPublishEndpoint> _publishEndpoint;
    private readonly NotificationQueueSender _notificationSender;
    private readonly LocationClient _locationClient;
    private readonly MetricsCollector _metrics;
    private readonly Mock<ILogger<ResetPasswordCommandHandler>> _logger;
    private readonly IdentityContext _context;

    public ResetPasswordCommandHandlerTests()
    {
        _context = TestHelper.CreateContext();
        _resetPasswordsStorage = new ResetPasswordsStorage(_context);
        _authPropsStorage = new AuthPropertiesStorage(_context);
        _usersClient = new Mock<UsersServerApi.UsersServerApiClient>();
        _requestContext = new RequestContext
        {
            DeviceName = "Dev", OperationSystem = "Win", AppName = "BF", AppVersion = "1.0", IpAddress = "1.1.1.1"
        };
        _publishEndpoint = new Mock<IPublishEndpoint>();
        _notificationSender = new NotificationQueueSender(_publishEndpoint.Object);
        _locationClient = TestHelper.CreateLocationClient();
        _metrics = new MetricsCollector();
        _logger = new Mock<ILogger<ResetPasswordCommandHandler>>();
    }

    private static RequestContext BuildRequestContext(
        string? deviceName = "Dev",
        string? os = "Win",
        string? appName = "BF",
        string? appVersion = "1.0",
        string? ipAddress = "1.1.1.1") => new()
    {
        DeviceName = deviceName,
        OperationSystem = os,
        AppName = appName,
        AppVersion = appVersion,
        IpAddress = ipAddress
    };

    private ResetPasswordCommandHandler CreateHandler(RequestContext? ctx = null)
    {
        return new ResetPasswordCommandHandler(
            _resetPasswordsStorage, _authPropsStorage, _usersClient.Object,
            ctx ?? _requestContext, _notificationSender, _locationClient, _metrics, _logger.Object);
    }

    [Fact]
    public async Task Handle_NoUsernameOrEmail_ThrowsNotSetUsernameOrEmailException()
    {
        var handler = CreateHandler();
        var cmd = new ResetPasswordCommand { OtpType = OtpType.Email };

        await Assert.ThrowsAsync<NotSetUsernameOrEmailException>(() => handler.Handle(cmd, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_NoDeviceName_ThrowsXDeviceNameIsRequiredException()
    {
        var handler = CreateHandler(BuildRequestContext(deviceName: null));
        var cmd = new ResetPasswordCommand { Username = "user", OtpType = OtpType.Email };

        await Assert.ThrowsAsync<XDeviceNameIsRequiredException>(() => handler.Handle(cmd, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_UserNotFound_ReturnsFakeResetId()
    {
        _usersClient
            .Setup(c => c.FindByLoginAsync(It.IsAny<FindByLoginRequest>(), null, null, CancellationToken.None))
            .Returns(new AsyncUnaryCall<FindByLoginResponse>(
                Task.FromResult(new FindByLoginResponse()),
                Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => new Metadata(), () => { }));

        var handler = CreateHandler();
        var cmd = new ResetPasswordCommand { Username = "nonexistent", OtpType = OtpType.Email };

        var result = await handler.Handle(cmd, CancellationToken.None);

        Assert.NotNull(result.ResetId);
        Assert.NotEqual(Guid.Empty.ToString(), result.ResetId);
    }

    [Fact]
    public async Task Handle_AuthenticatorOtpNotSetup_ThrowsOtpNotCreatedException()
    {
        _usersClient
            .Setup(c => c.FindByLoginAsync(It.IsAny<FindByLoginRequest>(), null, null, CancellationToken.None))
            .Returns(new AsyncUnaryCall<FindByLoginResponse>(
                Task.FromResult(new FindByLoginResponse { User = new User { Id = 1, Username = "user" } }),
                Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => new Metadata(), () => { }));

        var handler = CreateHandler();
        var cmd = new ResetPasswordCommand { Username = "user", OtpType = OtpType.Authenticator };

        await Assert.ThrowsAsync<OtpNotCreatedException>(() => handler.Handle(cmd, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_AuthenticatorOtp_CreatesResetPassword()
    {
        _usersClient
            .Setup(c => c.FindByLoginAsync(It.IsAny<FindByLoginRequest>(), null, null, CancellationToken.None))
            .Returns(new AsyncUnaryCall<FindByLoginResponse>(
                Task.FromResult(new FindByLoginResponse { User = new User { Id = 1, Username = "user" } }),
                Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => new Metadata(), () => { }));

        _context.AuthUserProperties.Add(new AuthUserProperty { UserId = 1, OtpEnabled = true });
        _context.SaveChanges();

        var handler = CreateHandler();
        var cmd = new ResetPasswordCommand { Username = "user", OtpType = OtpType.Authenticator };

        var result = await handler.Handle(cmd, CancellationToken.None);

        Assert.NotNull(result.ResetId);
        var reset = await _context.ResetPasswords.FirstOrDefaultAsync();
        Assert.NotNull(reset);
        Assert.Equal(OtpType.Authenticator, reset.OtpType);
    }

    [Fact]
    public async Task Handle_EmailOtp_SendsCodeAndCreatesResetPassword()
    {
        _usersClient
            .Setup(c => c.FindByLoginAsync(It.IsAny<FindByLoginRequest>(), null, null, CancellationToken.None))
            .Returns(new AsyncUnaryCall<FindByLoginResponse>(
                Task.FromResult(new FindByLoginResponse { User = new User { Id = 1, Username = "user" } }),
                Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => new Metadata(), () => { }));

        _usersClient
            .Setup(c => c.GetUserContactsAsync(It.IsAny<GetUserContactsRequest>(), null, null, CancellationToken.None))
            .Returns(new AsyncUnaryCall<GetUserContactsResponse>(
                Task.FromResult(new GetUserContactsResponse { User = new User { Id = 1 }, Contact = new UserContact { Email = "t@t.com" } }),
                Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => new Metadata(), () => { }));

        var handler = CreateHandler();
        var cmd = new ResetPasswordCommand { Username = "user", OtpType = OtpType.Email };

        var result = await handler.Handle(cmd, CancellationToken.None);

        Assert.NotNull(result.ResetId);
        _publishEndpoint.Verify(n => n.Publish(It.IsAny<BarkFluff.Shared.Queue.Notifications.EmailNotification>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_NoOs_ThrowsXOsNameIsRequiredException()
    {
        var handler = CreateHandler(BuildRequestContext(os: null));
        var cmd = new ResetPasswordCommand { Username = "user", OtpType = OtpType.Email };

        await Assert.ThrowsAsync<XOsNameIsRequiredException>(() => handler.Handle(cmd, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_NoAppInfo_ThrowsXAppInfoIsRequiedException()
    {
        var handler = CreateHandler(BuildRequestContext(appName: null));
        var cmd = new ResetPasswordCommand { Username = "user", OtpType = OtpType.Email };

        await Assert.ThrowsAsync<XAppInfoIsRequiedException>(() => handler.Handle(cmd, CancellationToken.None));
    }
}
