using BarkFluff.GrpcServer.Metrics;
using BarkFluff.GrpcServer.Tracker;
using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Identity.Domain;
using BarkFluff.Identity.Features.EnableOtpVerification;
using BarkFluff.Identity.Infrastructure;
using BarkFluff.Identity.Persistence.Contexts;
using BarkFluff.Identity.Persistence.Services;
using BarkFluff.Proto.Users;
using BarkFluff.Shared.Exceptions.Identity;
using BarkFluff.Shared.Queue.Notifications;

using Grpc.Core;

using MassTransit;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Moq;

using Xunit;

using User = BarkFluff.Proto.Users.User;

namespace BarkFluff.Identity.Tests.Features;

public class EnableOtpVerificationCommandHandlerTests
{
    private readonly UserContext _userContext;
    private readonly IdentityContext _context;
    private readonly AuthPropertiesStorage _authPropsStorage;
    private readonly Mock<UsersServerApi.UsersServerApiClient> _usersClient;
    private readonly Mock<IPublishEndpoint> _publishEndpoint;
    private readonly NotificationQueueSender _notificationSender;
    private readonly RequestContext _requestContext;
    private readonly LocationClient _locationClient;
    private readonly MetricsCollector _metrics;
    private readonly Mock<ILogger<EnableOtpVerificationCommandHandler>> _logger;

    public EnableOtpVerificationCommandHandlerTests()
    {
        _userContext = TestHelper.CreateUserContext(1);
        _context = TestHelper.CreateContext();
        _authPropsStorage = new AuthPropertiesStorage(_context);
        _usersClient = new Mock<UsersServerApi.UsersServerApiClient>();
        _publishEndpoint = new Mock<IPublishEndpoint>();
        _notificationSender = new NotificationQueueSender(_publishEndpoint.Object);
        _requestContext = new RequestContext
        {
            DeviceName = "Dev", OperationSystem = "Win", AppName = "BF", AppVersion = "1.0", IpAddress = "1.1.1.1"
        };
        _locationClient = TestHelper.CreateLocationClient();
        _metrics = new MetricsCollector();
        _logger = new Mock<ILogger<EnableOtpVerificationCommandHandler>>();

        _usersClient
            .Setup(c => c.GetByIdAsync(It.IsAny<GetByIdRequest>(), null, null, CancellationToken.None))
            .Returns(() =>
            {
                var resp = new AsyncUnaryCall<GetByIdResponse>(
                    Task.FromResult(new GetByIdResponse { User = new User { Id = 1, Username = "user" } }),
                    Task.FromResult(new Grpc.Core.Metadata()), () => Grpc.Core.Status.DefaultSuccess, () => new Grpc.Core.Metadata(), () => { });
                return resp;
            });
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

    private EnableOtpVerificationCommandHandler CreateHandler(RequestContext? ctx = null)
    {
        return new EnableOtpVerificationCommandHandler(
            _userContext, _authPropsStorage, _usersClient.Object,
            _notificationSender, ctx ?? _requestContext, _locationClient, _metrics, _logger.Object);
    }

    [Fact]
    public async Task Handle_NoDeviceName_ThrowsXDeviceNameIsRequiredException()
    {
        var handler = CreateHandler(BuildRequestContext(deviceName: null));
        var cmd = new EnableOtpVerificationCommand { OptType = BarkFluff.Proto.Identity.OtpTypeId.Authenticator };

        await Assert.ThrowsAsync<XDeviceNameIsRequiredException>(() => handler.Handle(cmd, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_NoOs_ThrowsXOsNameIsRequiredException()
    {
        var handler = CreateHandler(BuildRequestContext(os: null));
        var cmd = new EnableOtpVerificationCommand { OptType = BarkFluff.Proto.Identity.OtpTypeId.Authenticator };

        await Assert.ThrowsAsync<XOsNameIsRequiredException>(() => handler.Handle(cmd, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_Authenticator_ReturnsQrCode()
    {
        var handler = CreateHandler();
        var cmd = new EnableOtpVerificationCommand { OptType = BarkFluff.Proto.Identity.OtpTypeId.Authenticator };

        var result = await handler.Handle(cmd, CancellationToken.None);

        Assert.NotNull(result);
        Assert.False(string.IsNullOrEmpty(result.OtpQr));
        Assert.False(string.IsNullOrEmpty(result.OtpCode));
        var props = await _context.AuthUserProperties.FirstOrDefaultAsync(x => x.UserId == 1);
        Assert.NotNull(props);
        Assert.NotNull(props.OtpSecret);
        Assert.Equal(OtpType.Authenticator, props.SelectedOtpType);
    }

    [Fact]
    public async Task Handle_Email_SendsCodeAndReturnsEmptyQr()
    {
        _usersClient
            .Setup(c => c.GetUserContactsAsync(It.IsAny<GetUserContactsRequest>(), null, null, CancellationToken.None))
            .Returns(new AsyncUnaryCall<GetUserContactsResponse>(
                Task.FromResult(new GetUserContactsResponse
                {
                    User = new User { Id = 1 }, Contact = new UserContact { Email = "t@t.com" }
                }), Task.FromResult(new Grpc.Core.Metadata()), () => Grpc.Core.Status.DefaultSuccess, () => new Grpc.Core.Metadata(), () => { }));

        var handler = CreateHandler();
        var cmd = new EnableOtpVerificationCommand { OptType = BarkFluff.Proto.Identity.OtpTypeId.Email };

        var result = await handler.Handle(cmd, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(string.Empty, result.OtpQr);
        var props = await _context.AuthUserProperties.FirstOrDefaultAsync(x => x.UserId == 1);
        Assert.NotNull(props);
        Assert.NotNull(props.LastEmailAuthCode);
        Assert.Equal(OtpType.Email, props.SelectedOtpType);
        _publishEndpoint.Verify(p => p.Publish(It.IsAny<EmailNotification>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_UnknownType_ReturnsEmptyResponse()
    {
        var handler = CreateHandler();
        var cmd = new EnableOtpVerificationCommand { OptType = BarkFluff.Proto.Identity.OtpTypeId.Unknown };

        var result = await handler.Handle(cmd, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(string.Empty, result.OtpQr);
    }

    [Fact]
    public async Task Handle_NoAppInfo_ThrowsXAppInfoIsRequiedException()
    {
        var handler = CreateHandler(BuildRequestContext(appName: null));
        var cmd = new EnableOtpVerificationCommand { OptType = BarkFluff.Proto.Identity.OtpTypeId.Authenticator };

        await Assert.ThrowsAsync<XAppInfoIsRequiedException>(() => handler.Handle(cmd, CancellationToken.None));
    }
}
