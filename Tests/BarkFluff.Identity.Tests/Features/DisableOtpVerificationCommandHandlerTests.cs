using BarkFluff.GrpcServer.Metrics;
using BarkFluff.GrpcServer.Tracker;
using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Identity.Domain;
using BarkFluff.Identity.Features.DisableOtpVerification;
using BarkFluff.Identity.Infrastructure;
using BarkFluff.Identity.Persistence.Contexts;
using BarkFluff.Identity.Persistence.Services;
using BarkFluff.Identity.Security;
using BarkFluff.Shared.Exceptions.Identity;
using BarkFluff.Shared.Queue.Notifications;

using Grpc.Core;

using MassTransit;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Moq;

using OtpNet;

using Xunit;

namespace BarkFluff.Identity.Tests.Features;

public class DisableOtpVerificationCommandHandlerTests
{
    private readonly UserContext _userContext;
    private readonly IdentityContext _context;
    private readonly AuthPropertiesStorage _authPropsStorage;
    private readonly Mock<IPublishEndpoint> _publishEndpoint;
    private readonly NotificationQueueSender _notificationSender;
    private readonly LocationClient _locationClient;
    private readonly Mock<BarkFluff.Proto.Users.UsersServerApi.UsersServerApiClient> _usersClient;
    private readonly RequestContext _requestContext;
    private readonly MetricsCollector _metrics;
    private readonly Mock<ILogger<DisableOtpVerificationCommandHandler>> _logger;

    public DisableOtpVerificationCommandHandlerTests()
    {
        _userContext = TestHelper.CreateUserContext(1);
        _context = TestHelper.CreateContext();
        _authPropsStorage = new AuthPropertiesStorage(_context);
        _publishEndpoint = new Mock<IPublishEndpoint>();
        _notificationSender = new NotificationQueueSender(_publishEndpoint.Object);
        _locationClient = TestHelper.CreateLocationClient();
        _usersClient = new Mock<BarkFluff.Proto.Users.UsersServerApi.UsersServerApiClient>();
        _requestContext = new RequestContext { DeviceName = "Dev", IpAddress = "1.1.1.1" };
        _metrics = new MetricsCollector();
        _logger = new Mock<ILogger<DisableOtpVerificationCommandHandler>>();

        _usersClient
            .Setup(c => c.GetByIdAsync(It.IsAny<BarkFluff.Proto.Users.GetByIdRequest>(), null, null, CancellationToken.None))
            .Returns(new Grpc.Core.AsyncUnaryCall<BarkFluff.Proto.Users.GetByIdResponse>(
                Task.FromResult(new BarkFluff.Proto.Users.GetByIdResponse { User = new BarkFluff.Proto.Users.User { Id = 1, Username = "user" } }),
                Task.FromResult(new Grpc.Core.Metadata()), () => Grpc.Core.Status.DefaultSuccess, () => new Grpc.Core.Metadata(), () => { }));

        _usersClient
            .Setup(c => c.GetUserContactsAsync(It.IsAny<BarkFluff.Proto.Users.GetUserContactsRequest>(), null, null, CancellationToken.None))
            .Returns(new Grpc.Core.AsyncUnaryCall<BarkFluff.Proto.Users.GetUserContactsResponse>(
                Task.FromResult(new BarkFluff.Proto.Users.GetUserContactsResponse { User = new BarkFluff.Proto.Users.User { Id = 1 }, Contact = new BarkFluff.Proto.Users.UserContact { Email = "t@t.com" } }),
                Task.FromResult(new Grpc.Core.Metadata()), () => Grpc.Core.Status.DefaultSuccess, () => new Grpc.Core.Metadata(), () => { }));
    }

    private DisableOtpVerificationCommandHandler CreateHandler(TestHelper.TestIdentityAbuseGuard? abuseGuard = null)
    {
        return new DisableOtpVerificationCommandHandler(
            _userContext, _authPropsStorage, _notificationSender,
            _locationClient, _usersClient.Object, _requestContext, _metrics, _logger.Object,
            abuseGuard ?? TestHelper.CreateAbuseGuard());
    }

    [Fact]
    public async Task Handle_NullOtpConfigs_ThrowsOtpNotCreatedException()
    {
        var handler = CreateHandler();
        var cmd = new DisableOtpVerificationCommand { OptType = BarkFluff.Proto.Identity.OtpTypeId.Authenticator, OtpCode = "000000" };

        await Assert.ThrowsAsync<BarkFluff.Identity.Persistence.Exceptions.OtpNotCreatedException>(() => handler.Handle(cmd, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_AuthenticatorNotEnabled_ThrowsOtpNotCreatedException()
    {
        _context.AuthUserProperties.Add(new AuthUserProperty { UserId = 1, OtpEnabled = false, OtpSecret = Base32Encoding.ToString(KeyGeneration.GenerateRandomKey(20)) });
        _context.SaveChanges();

        var handler = CreateHandler();
        var cmd = new DisableOtpVerificationCommand { OptType = BarkFluff.Proto.Identity.OtpTypeId.Authenticator, OtpCode = "000000" };

        await Assert.ThrowsAsync<BarkFluff.Identity.Persistence.Exceptions.OtpNotCreatedException>(() => handler.Handle(cmd, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_AuthenticatorInvalidCode_ThrowsNotValidOtpCodeException()
    {
        _context.AuthUserProperties.Add(new AuthUserProperty { UserId = 1, OtpEnabled = true, OtpSecret = Base32Encoding.ToString(KeyGeneration.GenerateRandomKey(20)) });
        _context.SaveChanges();

        var handler = CreateHandler();
        var cmd = new DisableOtpVerificationCommand { OptType = BarkFluff.Proto.Identity.OtpTypeId.Authenticator, OtpCode = "000000" };

        await Assert.ThrowsAsync<NotValidOtpCodeException>(() => handler.Handle(cmd, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_FifthWrongOtp_LocksDisableOperationAndDoesNotNotify()
    {
        _context.AuthUserProperties.Add(new AuthUserProperty
        {
            UserId = 1,
            OtpEnabled = true,
            OtpSecret = Base32Encoding.ToString(KeyGeneration.GenerateRandomKey(20))
        });
        _context.SaveChanges();

        var abuseGuard = TestHelper.CreateAbuseGuard();
        abuseGuard.OtpFailureResult = new IdentityFailureResult(5, true);
        var handler = CreateHandler(abuseGuard);
        var command = new DisableOtpVerificationCommand
        {
            OptType = BarkFluff.Proto.Identity.OtpTypeId.Authenticator,
            OtpCode = "000000"
        };

        await Assert.ThrowsAsync<IdentityLockoutException>(() => handler.Handle(command, CancellationToken.None));
        await Assert.ThrowsAsync<IdentityLockoutException>(() => handler.Handle(command, CancellationToken.None));

        var props = await _context.AuthUserProperties.FirstAsync(x => x.UserId == 1);
        Assert.True(props.OtpEnabled);
        _publishEndpoint.Verify(
            p => p.Publish(It.IsAny<EmailNotification>(), It.IsAny<CancellationToken>()),
            Times.Never);
        Assert.Equal(1, abuseGuard.OtpFailureCalls);
    }

    [Fact]
    public async Task Handle_MissingAuthenticatorOtp_DoesNotRegisterFailure()
    {
        _context.AuthUserProperties.Add(new AuthUserProperty
        {
            UserId = 1,
            OtpEnabled = true,
            OtpSecret = Base32Encoding.ToString(KeyGeneration.GenerateRandomKey(20))
        });
        _context.SaveChanges();

        var abuseGuard = TestHelper.CreateAbuseGuard();
        var handler = CreateHandler(abuseGuard);

        await Assert.ThrowsAsync<OtpCodeNeedException>(() => handler.Handle(
            new DisableOtpVerificationCommand { OptType = BarkFluff.Proto.Identity.OtpTypeId.Authenticator },
            CancellationToken.None));

        Assert.Equal(0, abuseGuard.OtpFailureCalls);
    }

    [Fact]
    public async Task Handle_AuthenticatorValidCode_DisablesOtp()
    {
        var key = KeyGeneration.GenerateRandomKey(20);
        var base32 = Base32Encoding.ToString(key);
        var totp = new Totp(key);
        var validCode = totp.ComputeTotp();

        _context.AuthUserProperties.Add(new AuthUserProperty { UserId = 1, OtpEnabled = true, OtpSecret = base32 });
        _context.SaveChanges();

        var handler = CreateHandler();
        var cmd = new DisableOtpVerificationCommand { OptType = BarkFluff.Proto.Identity.OtpTypeId.Authenticator, OtpCode = validCode };

        var result = await handler.Handle(cmd, CancellationToken.None);

        Assert.NotNull(result);
        var props = await _context.AuthUserProperties.FirstAsync(x => x.UserId == 1);
        Assert.False(props.OtpEnabled);
    }

    [Fact]
    public async Task Handle_Email_DisablesEmailOtpWithoutCode()
    {
        _context.AuthUserProperties.Add(new AuthUserProperty { UserId = 1, EmailOtpEnabled = true });
        _context.SaveChanges();

        var handler = CreateHandler();
        var cmd = new DisableOtpVerificationCommand { OptType = BarkFluff.Proto.Identity.OtpTypeId.Email };

        var result = await handler.Handle(cmd, CancellationToken.None);

        Assert.NotNull(result);
        var props = await _context.AuthUserProperties.FirstAsync(x => x.UserId == 1);
        Assert.False(props.EmailOtpEnabled);
    }

    [Fact]
    public async Task Handle_AuthenticatorValidCode_SendsNotification()
    {
        var key = KeyGeneration.GenerateRandomKey(20);
        var base32 = Base32Encoding.ToString(key);
        var totp = new Totp(key);
        var validCode = totp.ComputeTotp();

        _context.AuthUserProperties.Add(new AuthUserProperty { UserId = 1, OtpEnabled = true, OtpSecret = base32 });
        _context.SaveChanges();

        var handler = CreateHandler();
        var cmd = new DisableOtpVerificationCommand { OptType = BarkFluff.Proto.Identity.OtpTypeId.Authenticator, OtpCode = validCode };

        var result = await handler.Handle(cmd, CancellationToken.None);

        Assert.NotNull(result);
        _publishEndpoint.Verify(p => p.Publish(It.IsAny<EmailNotification>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_Email_SendsNotification()
    {
        _context.AuthUserProperties.Add(new AuthUserProperty { UserId = 1, EmailOtpEnabled = true });
        _context.SaveChanges();

        var handler = CreateHandler();
        var cmd = new DisableOtpVerificationCommand { OptType = BarkFluff.Proto.Identity.OtpTypeId.Email };

        var result = await handler.Handle(cmd, CancellationToken.None);

        Assert.NotNull(result);
        _publishEndpoint.Verify(p => p.Publish(It.IsAny<EmailNotification>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
