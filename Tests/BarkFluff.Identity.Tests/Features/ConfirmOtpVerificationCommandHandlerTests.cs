using BarkFluff.GrpcServer.Metrics;
using BarkFluff.GrpcServer.Tracker;
using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Identity.Domain;
using DomainOtpType = BarkFluff.Identity.Domain.OtpType;
using BarkFluff.Identity.Features.ConfirmOtpVerification;
using BarkFluff.Identity.Infrastructure;
using BarkFluff.Identity.Persistence.Contexts;
using BarkFluff.Identity.Persistence.Services;
using BarkFluff.Identity.Security;
using BarkFluff.Proto.Users;
using BarkFluff.Shared.Exceptions.Identity;

using BarkFluff.Shared.Queue.Notifications;

using Grpc.Core;

using MassTransit;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Moq;

using OtpNet;

using Xunit;

using User = BarkFluff.Proto.Users.User;

namespace BarkFluff.Identity.Tests.Features;

public class ConfirmOtpVerificationCommandHandlerTests
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
    private readonly Mock<ILogger<ConfirmOtpVerificationCommandHandler>> _logger;

    public ConfirmOtpVerificationCommandHandlerTests()
    {
        _userContext = TestHelper.CreateUserContext(1);
        _context = TestHelper.CreateContext();
        _authPropsStorage = new AuthPropertiesStorage(_context);
        _usersClient = new Mock<UsersServerApi.UsersServerApiClient>();
        _publishEndpoint = new Mock<IPublishEndpoint>();
        _notificationSender = new NotificationQueueSender(_publishEndpoint.Object);
        _requestContext = new RequestContext { DeviceName = "Dev", IpAddress = "1.1.1.1" };
        _locationClient = TestHelper.CreateLocationClient();
        _metrics = new MetricsCollector();
        _logger = new Mock<ILogger<ConfirmOtpVerificationCommandHandler>>();

        _usersClient
            .Setup(c => c.GetByIdAsync(It.IsAny<GetByIdRequest>(), null, null, CancellationToken.None))
            .Returns(new AsyncUnaryCall<GetByIdResponse>(
                Task.FromResult(new GetByIdResponse { User = new User { Id = 1, Username = "user" } }),
                Task.FromResult(new Grpc.Core.Metadata()), () => Grpc.Core.Status.DefaultSuccess, () => new Grpc.Core.Metadata(), () => { }));

        _usersClient
            .Setup(c => c.GetUserContactsAsync(It.IsAny<GetUserContactsRequest>(), null, null, CancellationToken.None))
            .Returns(new AsyncUnaryCall<GetUserContactsResponse>(
                Task.FromResult(new GetUserContactsResponse { User = new User { Id = 1 }, Contact = new UserContact { Email = "t@t.com" } }),
                Task.FromResult(new Grpc.Core.Metadata()), () => Grpc.Core.Status.DefaultSuccess, () => new Grpc.Core.Metadata(), () => { }));
    }

    private ConfirmOtpVerificationCommandHandler CreateHandler(TestHelper.TestIdentityAbuseGuard? abuseGuard = null)
    {
        return new ConfirmOtpVerificationCommandHandler(
            _userContext, _authPropsStorage, _usersClient.Object,
            _notificationSender, _requestContext, _locationClient, _metrics, _logger.Object,
            abuseGuard ?? TestHelper.CreateAbuseGuard());
    }

    [Fact]
    public async Task Handle_AuthenticatorValidCode_EnablesOtp()
    {
        var secretKey = KeyGeneration.GenerateRandomKey(20);
        var base32Secret = Base32Encoding.ToString(secretKey);
        var totp = new Totp(secretKey);
        var validCode = totp.ComputeTotp();

        _context.AuthUserProperties.Add(new AuthUserProperty
        {
            UserId = 1,
            SelectedOtpType = DomainOtpType.Authenticator,
            OtpEnabled = false,
            OtpSecret = base32Secret
        });
        _context.SaveChanges();

        var handler = CreateHandler();
        var cmd = new ConfirmOtpVerificationCommand { OtpCode = validCode };

        var result = await handler.Handle(cmd, CancellationToken.None);

        Assert.NotNull(result);
        var props = await _context.AuthUserProperties.FirstAsync(x => x.UserId == 1);
        Assert.True(props.OtpEnabled);
    }

    [Fact]
    public async Task Handle_AuthenticatorInvalidCode_ThrowsNotValidOtpCodeException()
    {
        _context.AuthUserProperties.Add(new AuthUserProperty
        {
            UserId = 1,
            SelectedOtpType = DomainOtpType.Authenticator,
            OtpSecret = Base32Encoding.ToString(KeyGeneration.GenerateRandomKey(20))
        });
        _context.SaveChanges();

        var handler = CreateHandler();
        var cmd = new ConfirmOtpVerificationCommand { OtpCode = "000000" };

        await Assert.ThrowsAsync<NotValidOtpCodeException>(() => handler.Handle(cmd, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_FifthWrongOtp_LocksSetupAndDoesNotNotify()
    {
        _context.AuthUserProperties.Add(new AuthUserProperty
        {
            UserId = 1,
            SelectedOtpType = DomainOtpType.Authenticator,
            OtpSecret = Base32Encoding.ToString(KeyGeneration.GenerateRandomKey(20))
        });
        _context.SaveChanges();

        var abuseGuard = TestHelper.CreateAbuseGuard();
        abuseGuard.OtpFailureResult = new IdentityFailureResult(5, true);
        var handler = CreateHandler(abuseGuard);

        await Assert.ThrowsAsync<IdentityLockoutException>(() => handler.Handle(
            new ConfirmOtpVerificationCommand { OtpCode = "000000" },
            CancellationToken.None));
        await Assert.ThrowsAsync<IdentityLockoutException>(() => handler.Handle(
            new ConfirmOtpVerificationCommand { OtpCode = "000000" },
            CancellationToken.None));

        _publishEndpoint.Verify(
            p => p.Publish(It.IsAny<EmailNotification>(), It.IsAny<CancellationToken>()),
            Times.Never);
        Assert.Equal(1, abuseGuard.OtpFailureCalls);
    }

    [Fact]
    public async Task Handle_MissingOtp_DoesNotRegisterFailure()
    {
        _context.AuthUserProperties.Add(new AuthUserProperty
        {
            UserId = 1,
            SelectedOtpType = DomainOtpType.Authenticator,
            OtpSecret = Base32Encoding.ToString(KeyGeneration.GenerateRandomKey(20))
        });
        _context.SaveChanges();

        var abuseGuard = TestHelper.CreateAbuseGuard();
        var handler = CreateHandler(abuseGuard);

        await Assert.ThrowsAsync<OtpCodeNeedException>(() => handler.Handle(
            new ConfirmOtpVerificationCommand(),
            CancellationToken.None));

        Assert.Equal(0, abuseGuard.OtpFailureCalls);
    }

    [Fact]
    public async Task Handle_EmailValidCode_EnablesEmailOtp()
    {
        _context.AuthUserProperties.Add(new AuthUserProperty
        {
            UserId = 1,
            SelectedOtpType = DomainOtpType.Email,
            LastEmailAuthCode = "123456"
        });
        _context.SaveChanges();

        var handler = CreateHandler();
        var cmd = new ConfirmOtpVerificationCommand { OtpCode = "123456" };

        var result = await handler.Handle(cmd, CancellationToken.None);

        Assert.NotNull(result);
        var props = await _context.AuthUserProperties.FirstAsync(x => x.UserId == 1);
        Assert.True(props.EmailOtpEnabled);
    }

    [Fact]
    public async Task Handle_EmailInvalidCode_ThrowsNotValidOtpCodeException()
    {
        _context.AuthUserProperties.Add(new AuthUserProperty
        {
            UserId = 1,
            SelectedOtpType = DomainOtpType.Email,
            LastEmailAuthCode = "123456"
        });
        _context.SaveChanges();

        var handler = CreateHandler();
        var cmd = new ConfirmOtpVerificationCommand { OtpCode = "654321" };

        await Assert.ThrowsAsync<NotValidOtpCodeException>(() => handler.Handle(cmd, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_UnknownOtpType_ReturnsEmptyResponse()
    {
        _context.AuthUserProperties.Add(new AuthUserProperty
        {
            UserId = 1,
            SelectedOtpType = DomainOtpType.Unknown
        });
        _context.SaveChanges();

        var handler = CreateHandler();
        var cmd = new ConfirmOtpVerificationCommand { OtpCode = "000000" };

        var result = await handler.Handle(cmd, CancellationToken.None);

        Assert.NotNull(result);
    }

    [Fact]
    public async Task Handle_AuthenticatorValidCode_SendsTwoFactorChangedNotification()
    {
        var secretKey = KeyGeneration.GenerateRandomKey(20);
        var base32Secret = Base32Encoding.ToString(secretKey);
        var totp = new Totp(secretKey);
        var validCode = totp.ComputeTotp();

        _context.AuthUserProperties.Add(new AuthUserProperty
        {
            UserId = 1,
            SelectedOtpType = DomainOtpType.Authenticator,
            OtpEnabled = false,
            OtpSecret = base32Secret
        });
        _context.SaveChanges();

        var handler = CreateHandler();
        var cmd = new ConfirmOtpVerificationCommand { OtpCode = validCode };

        var result = await handler.Handle(cmd, CancellationToken.None);

        Assert.NotNull(result);
        _publishEndpoint.Verify(p => p.Publish(It.IsAny<EmailNotification>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_EmailValidCode_SendsTwoFactorChangedNotification()
    {
        _context.AuthUserProperties.Add(new AuthUserProperty
        {
            UserId = 1,
            SelectedOtpType = DomainOtpType.Email,
            LastEmailAuthCode = "123456"
        });
        _context.SaveChanges();

        var handler = CreateHandler();
        var cmd = new ConfirmOtpVerificationCommand { OtpCode = "123456" };

        var result = await handler.Handle(cmd, CancellationToken.None);

        Assert.NotNull(result);
        _publishEndpoint.Verify(p => p.Publish(It.IsAny<EmailNotification>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
