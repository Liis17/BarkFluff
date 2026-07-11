using BarkFluff.GrpcServer.Metrics;
using BarkFluff.GrpcServer.Tracker;
using BarkFluff.Identity.Domain;
using BarkFluff.Identity.Features.Auth;
using BarkFluff.Identity.Features.CreateToken;
using BarkFluff.Identity.Infrastructure;
using BarkFluff.Identity.Persistence.Contexts;
using BarkFluff.Identity.Persistence.Services;
using BarkFluff.Identity.Services;
using BarkFluff.Proto.Identity;
using BarkFluff.Proto.Users;
using BarkFluff.Shared.Exceptions.Identity;
using BarkFluff.Shared.Queue.Notifications;

using Grpc.Core;

using MassTransit;

using MediatR;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Moq;

using OtpNet;

using Xunit;

using User = BarkFluff.Proto.Users.User;

namespace BarkFluff.Identity.Tests.Features;

public class AuthCommandHandlerTests
{
    private readonly Mock<UsersServerApi.UsersServerApiClient> _usersClient;
    private readonly Mock<IMediator> _mediator;
    private readonly Mock<IPublishEndpoint> _publishEndpoint;
    private readonly MetricsCollector _metrics;
    private readonly NotificationQueueSender _notificationSender;
    private readonly LocationClient _locationClient;
    private readonly Mock<ILogger<AuthCommandHandler>> _logger;
    private readonly RequestContext _requestContext;
    private readonly IdentityContext _context;
    private readonly AuthPropertiesStorage _authPropsStorage;
    private readonly RefreshTokensStorage _refreshTokensStorage;
    private readonly PasswordsStorage _passwordsStorage;

    public AuthCommandHandlerTests()
    {
        _usersClient = new Mock<UsersServerApi.UsersServerApiClient>();
        _mediator = new Mock<IMediator>();
        _publishEndpoint = new Mock<IPublishEndpoint>();
        _metrics = new MetricsCollector();
        _notificationSender = new NotificationQueueSender(_publishEndpoint.Object);
        _locationClient = TestHelper.CreateLocationClient();
        _logger = new Mock<ILogger<AuthCommandHandler>>();
        _requestContext = new RequestContext
        {
            DeviceName = "TestDevice",
            OperationSystem = "Windows",
            AppName = "BarkFluff",
            AppVersion = "1.0",
            DeviceId = "dev-123",
            IpAddress = "127.0.0.1"
        };

        _context = TestHelper.CreateContext();
        _authPropsStorage = new AuthPropertiesStorage(_context);
        _refreshTokensStorage = new RefreshTokensStorage(_context);
        _passwordsStorage = new PasswordsStorage(_context);
    }

    private static RequestContext BuildRequestContext(
        string? deviceName = "TestDevice",
        string? os = "Windows",
        string? appName = "BarkFluff",
        string? appVersion = "1.0",
        string? deviceId = "dev-123",
        string? ipAddress = "127.0.0.1") => new()
    {
        DeviceName = deviceName,
        OperationSystem = os,
        AppName = appName,
        AppVersion = appVersion,
        DeviceId = deviceId,
        IpAddress = ipAddress
    };

    private AuthCommandHandler CreateHandler(RequestContext? ctx = null)
    {
        return new AuthCommandHandler(
            _usersClient.Object, _mediator.Object, _authPropsStorage,
            _notificationSender, _refreshTokensStorage, ctx ?? _requestContext,
            _passwordsStorage, _locationClient, _metrics, _logger.Object);
    }

    [Fact]
    public async Task Handle_NoUsernameOrEmail_ThrowsNotSetUsernameOrEmailException()
    {
        var handler = CreateHandler();
        var command = new AuthCommand { Password = "pass" };

        await Assert.ThrowsAsync<NotSetUsernameOrEmailException>(() => handler.Handle(command, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_NoPassword_ThrowsInvalidLoginOrPasswordException()
    {
        var handler = CreateHandler();
        var command = new AuthCommand { Username = "user" };

        await Assert.ThrowsAsync<InvalidLoginOrPasswordException>(() => handler.Handle(command, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_NoDeviceName_ThrowsXDeviceNameIsRequiredException()
    {
        var handler = CreateHandler(BuildRequestContext(deviceName: null));
        var command = new AuthCommand { Username = "user", Password = "pass" };

        await Assert.ThrowsAsync<XDeviceNameIsRequiredException>(() => handler.Handle(command, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_NoOs_ThrowsXOsNameIsRequiredException()
    {
        var handler = CreateHandler(BuildRequestContext(os: null));
        var command = new AuthCommand { Username = "user", Password = "pass" };

        await Assert.ThrowsAsync<XOsNameIsRequiredException>(() => handler.Handle(command, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_NoAppInfo_ThrowsXAppInfoIsRequiedException()
    {
        var handler = CreateHandler(BuildRequestContext(appName: null));
        var command = new AuthCommand { Username = "user", Password = "pass" };

        await Assert.ThrowsAsync<XAppInfoIsRequiedException>(() => handler.Handle(command, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_UserNotFound_ThrowsInvalidLoginOrPasswordException()
    {
        _usersClient
            .Setup(c => c.FindByLoginAsync(It.IsAny<FindByLoginRequest>(), null, null, CancellationToken.None))
            .Returns(new AsyncUnaryCall<FindByLoginResponse>(
                Task.FromResult(new FindByLoginResponse()),
                Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => new Metadata(), () => { }));

        var handler = CreateHandler();
        var command = new AuthCommand { Username = "user", Password = "pass" };

        await Assert.ThrowsAsync<InvalidLoginOrPasswordException>(() => handler.Handle(command, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_Bot_ThrowsInvalidLoginOrPasswordExceptionWithoutSideEffects()
    {
        _usersClient
            .Setup(c => c.FindByLoginAsync(It.IsAny<FindByLoginRequest>(), null, null, CancellationToken.None))
            .Returns(new AsyncUnaryCall<FindByLoginResponse>(
                Task.FromResult(new FindByLoginResponse { User = new User { Id = 1, IsBot = true } }),
                Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => new Metadata(), () => { }));

        var handler = CreateHandler();

        await Assert.ThrowsAsync<InvalidLoginOrPasswordException>(() =>
            handler.Handle(new AuthCommand { Username = "testbot", Password = "password" }, CancellationToken.None));

        Assert.Empty(_context.RefreshTokens);
        _mediator.Verify(m => m.Send(It.IsAny<CreateTokenCommand>(), It.IsAny<CancellationToken>()), Times.Never);
        _publishEndpoint.Verify(p => p.Publish(It.IsAny<EmailNotification>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_OtpEnabledNoOtpCode_ThrowsOtpCodeNeedException()
    {
        var user = new FindByLoginResponse();
        user.User = new User { Id = 1, Username = "user" };

        _usersClient
            .Setup(c => c.FindByLoginAsync(It.IsAny<FindByLoginRequest>(), null, null, CancellationToken.None))
            .Returns(new AsyncUnaryCall<FindByLoginResponse>(
                Task.FromResult(user), Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => new Metadata(), () => { }));

        _context.AuthUserProperties.Add(new AuthUserProperty { UserId = 1, OtpEnabled = true, OtpSecret = "SECRET" });
        _context.SaveChanges();

        var handler = CreateHandler();
        var command = new AuthCommand { Username = "user", Password = "pass" };

        await Assert.ThrowsAsync<OtpCodeNeedException>(() => handler.Handle(command, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_EmailOtpEnabledNoCode_SendsEmailAndThrowsOtpCodeNeedException()
    {
        var user = new FindByLoginResponse();
        user.User = new User { Id = 1, Username = "user" };

        _usersClient
            .Setup(c => c.FindByLoginAsync(It.IsAny<FindByLoginRequest>(), null, null, CancellationToken.None))
            .Returns(new AsyncUnaryCall<FindByLoginResponse>(
                Task.FromResult(user), Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => new Metadata(), () => { }));

        _usersClient
            .Setup(c => c.GetUserContactsAsync(It.IsAny<GetUserContactsRequest>(), null, null, CancellationToken.None))
            .Returns(new AsyncUnaryCall<GetUserContactsResponse>(
                Task.FromResult(new GetUserContactsResponse
                {
                    User = new User { Id = 1 },
                    Contact = new UserContact { Email = "test@test.com" }
                }), Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => new Metadata(), () => { }));

        _context.AuthUserProperties.Add(new AuthUserProperty { UserId = 1, OtpEnabled = false, EmailOtpEnabled = true });
        _context.SaveChanges();

        var handler = CreateHandler();
        var command = new AuthCommand { Username = "user", Password = "pass" };

        await Assert.ThrowsAsync<OtpCodeNeedException>(() => handler.Handle(command, CancellationToken.None));

        var props = _context.AuthUserProperties.First(x => x.UserId == 1);
        Assert.NotNull(props.LastEmailAuthCode);
        _publishEndpoint.Verify(p => p.Publish(It.IsAny<EmailNotification>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WrongPassword_ThrowsInvalidLoginOrPasswordException()
    {
        var user = new FindByLoginResponse();
        user.User = new User { Id = 1, Username = "user" };

        _usersClient
            .Setup(c => c.FindByLoginAsync(It.IsAny<FindByLoginRequest>(), null, null, CancellationToken.None))
            .Returns(new AsyncUnaryCall<FindByLoginResponse>(
                Task.FromResult(user), Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => new Metadata(), () => { }));

        _usersClient
            .Setup(c => c.GetUserContactsAsync(It.IsAny<GetUserContactsRequest>(), null, null, CancellationToken.None))
            .Returns(new AsyncUnaryCall<GetUserContactsResponse>(
                Task.FromResult(new GetUserContactsResponse
                {
                    User = new User { Id = 1 },
                    Contact = new UserContact { Email = "test@test.com" }
                }), Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => new Metadata(), () => { }));

        _context.UserPasswords.Add(new UserPassword { UserId = 1, PasswordHash = PasswordHasher.HashPassword("correctpassword") });
        _context.SaveChanges();

        var handler = CreateHandler();
        var command = new AuthCommand { Username = "user", Password = "wrongpassword" };

        await Assert.ThrowsAsync<InvalidLoginOrPasswordException>(() => handler.Handle(command, CancellationToken.None));
        _publishEndpoint.Verify(p => p.Publish(It.IsAny<EmailNotification>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_SuccessfulLogin_ReturnsAuthResponse()
    {
        var user = new FindByLoginResponse();
        user.User = new User { Id = 1, Username = "user" };

        _usersClient
            .Setup(c => c.FindByLoginAsync(It.IsAny<FindByLoginRequest>(), null, null, CancellationToken.None))
            .Returns(new AsyncUnaryCall<FindByLoginResponse>(
                Task.FromResult(user), Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => new Metadata(), () => { }));

        _usersClient
            .Setup(c => c.GetUserContactsAsync(It.IsAny<GetUserContactsRequest>(), null, null, CancellationToken.None))
            .Returns(new AsyncUnaryCall<GetUserContactsResponse>(
                Task.FromResult(new GetUserContactsResponse
                {
                    User = new User { Id = 1, Username = "user" },
                    Contact = new UserContact { Email = "test@test.com" }
                }), Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => new Metadata(), () => { }));

        _usersClient
            .Setup(c => c.RegisterDeviceAsync(It.IsAny<RegisterDeviceRequest>(), null, null, CancellationToken.None))
            .Returns(new AsyncUnaryCall<RegisterDeviceResponse>(
                Task.FromResult(new RegisterDeviceResponse()), Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => new Metadata(), () => { }));

        _context.UserPasswords.Add(new UserPassword { UserId = 1, PasswordHash = PasswordHasher.HashPassword("password123") });
        _context.SaveChanges();

        _mediator.Setup(m => m.Send(It.IsAny<CreateTokenCommand>(), CancellationToken.None))
            .ReturnsAsync(new CreateTokenResponse
            {
                AccessToken = new Token { Value = "access", ExpirationDate = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.UtcNow.AddMinutes(30)) }
            });

        var handler = CreateHandler();
        var command = new AuthCommand { Username = "user", Password = "password123" };

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.NotNull(result);
        Assert.NotNull(result.RefreshToken);
        Assert.NotNull(result.AccessToken);
        Assert.Equal("access", result.AccessToken.Value);
        _publishEndpoint.Verify(p => p.Publish(It.IsAny<EmailNotification>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_FallbackDeviceId_WhenDeviceIdIsNull()
    {
        var user = new FindByLoginResponse();
        user.User = new User { Id = 1, Username = "user" };

        _usersClient
            .Setup(c => c.FindByLoginAsync(It.IsAny<FindByLoginRequest>(), null, null, CancellationToken.None))
            .Returns(new AsyncUnaryCall<FindByLoginResponse>(
                Task.FromResult(user), Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => new Metadata(), () => { }));

        _usersClient
            .Setup(c => c.GetUserContactsAsync(It.IsAny<GetUserContactsRequest>(), null, null, CancellationToken.None))
            .Returns(new AsyncUnaryCall<GetUserContactsResponse>(
                Task.FromResult(new GetUserContactsResponse
                {
                    User = new User { Id = 1, Username = "user" },
                    Contact = new UserContact { Email = "test@test.com" }
                }), Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => new Metadata(), () => { }));

        _usersClient
            .Setup(c => c.RegisterDeviceAsync(It.IsAny<RegisterDeviceRequest>(), null, null, CancellationToken.None))
            .Returns(new AsyncUnaryCall<RegisterDeviceResponse>(
                Task.FromResult(new RegisterDeviceResponse()), Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => new Metadata(), () => { }));

        _context.UserPasswords.Add(new UserPassword { UserId = 1, PasswordHash = PasswordHasher.HashPassword("password123") });
        _context.SaveChanges();

        _mediator.Setup(m => m.Send(It.IsAny<CreateTokenCommand>(), CancellationToken.None))
            .ReturnsAsync(new CreateTokenResponse { AccessToken = new Token() });

        var handler = CreateHandler(BuildRequestContext(deviceId: null));
        var command = new AuthCommand { Username = "user", Password = "password123" };

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.NotNull(result);
        var tokens = await _context.RefreshTokens.ToListAsync();
        Assert.Single(tokens);
    }

    [Fact]
    public async Task Handle_LoginWithEmail_Works()
    {
        var user = new FindByLoginResponse();
        user.User = new User { Id = 1, Username = "user" };

        _usersClient
            .Setup(c => c.FindByLoginAsync(It.IsAny<FindByLoginRequest>(), null, null, CancellationToken.None))
            .Returns(new AsyncUnaryCall<FindByLoginResponse>(
                Task.FromResult(user), Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => new Metadata(), () => { }));

        _usersClient
            .Setup(c => c.GetUserContactsAsync(It.IsAny<GetUserContactsRequest>(), null, null, CancellationToken.None))
            .Returns(new AsyncUnaryCall<GetUserContactsResponse>(
                Task.FromResult(new GetUserContactsResponse
                {
                    User = new User { Id = 1, Username = "user" },
                    Contact = new UserContact { Email = "test@test.com" }
                }), Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => new Metadata(), () => { }));

        _usersClient
            .Setup(c => c.RegisterDeviceAsync(It.IsAny<RegisterDeviceRequest>(), null, null, CancellationToken.None))
            .Returns(new AsyncUnaryCall<RegisterDeviceResponse>(
                Task.FromResult(new RegisterDeviceResponse()), Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => new Metadata(), () => { }));

        _context.UserPasswords.Add(new UserPassword { UserId = 1, PasswordHash = PasswordHasher.HashPassword("pass") });
        _context.SaveChanges();

        _mediator.Setup(m => m.Send(It.IsAny<CreateTokenCommand>(), CancellationToken.None))
            .ReturnsAsync(new CreateTokenResponse { AccessToken = new Token() });

        var handler = CreateHandler();
        var command = new AuthCommand { Email = "test@test.com", Password = "pass" };

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.NotNull(result);
    }

    [Fact]
    public async Task Handle_BothOtpTypesEnabledNoCode_ThrowsOtpCodeNeedException()
    {
        var user = new FindByLoginResponse();
        user.User = new User { Id = 1, Username = "user" };

        _usersClient
            .Setup(c => c.FindByLoginAsync(It.IsAny<FindByLoginRequest>(), null, null, CancellationToken.None))
            .Returns(new AsyncUnaryCall<FindByLoginResponse>(
                Task.FromResult(user), Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => new Metadata(), () => { }));

        _context.AuthUserProperties.Add(new AuthUserProperty { UserId = 1, OtpEnabled = true, EmailOtpEnabled = true, OtpSecret = "SECRET" });
        _context.SaveChanges();

        var handler = CreateHandler();
        var command = new AuthCommand { Username = "user", Password = "pass" };

        await Assert.ThrowsAsync<OtpCodeNeedException>(() => handler.Handle(command, CancellationToken.None));

        _publishEndpoint.Verify(p => p.Publish(It.IsAny<EmailNotification>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_AuthenticatorOtpValidCode_ProceedsToLogin()
    {
        var secretKey = KeyGeneration.GenerateRandomKey(20);
        var base32Secret = Base32Encoding.ToString(secretKey);
        var totp = new Totp(secretKey);
        var validCode = totp.ComputeTotp();

        var user = new FindByLoginResponse();
        user.User = new User { Id = 1, Username = "user" };

        _usersClient
            .Setup(c => c.FindByLoginAsync(It.IsAny<FindByLoginRequest>(), null, null, CancellationToken.None))
            .Returns(new AsyncUnaryCall<FindByLoginResponse>(
                Task.FromResult(user), Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => new Metadata(), () => { }));

        _usersClient
            .Setup(c => c.GetUserContactsAsync(It.IsAny<GetUserContactsRequest>(), null, null, CancellationToken.None))
            .Returns(new AsyncUnaryCall<GetUserContactsResponse>(
                Task.FromResult(new GetUserContactsResponse
                {
                    User = new User { Id = 1, Username = "user" },
                    Contact = new UserContact { Email = "test@test.com" }
                }), Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => new Metadata(), () => { }));

        _usersClient
            .Setup(c => c.RegisterDeviceAsync(It.IsAny<RegisterDeviceRequest>(), null, null, CancellationToken.None))
            .Returns(new AsyncUnaryCall<RegisterDeviceResponse>(
                Task.FromResult(new RegisterDeviceResponse()), Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => new Metadata(), () => { }));

        _context.UserPasswords.Add(new UserPassword { UserId = 1, PasswordHash = PasswordHasher.HashPassword("password123") });
        _context.AuthUserProperties.Add(new AuthUserProperty { UserId = 1, OtpEnabled = true, OtpSecret = base32Secret });
        _context.SaveChanges();

        _mediator.Setup(m => m.Send(It.IsAny<CreateTokenCommand>(), CancellationToken.None))
            .ReturnsAsync(new CreateTokenResponse
            {
                AccessToken = new Token { Value = "access", ExpirationDate = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.UtcNow.AddMinutes(30)) }
            });

        var handler = CreateHandler();
        var command = new AuthCommand { Username = "user", Password = "password123", OtpCode = validCode };

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.NotNull(result);
        Assert.NotNull(result.RefreshToken);
        Assert.Equal("access", result.AccessToken.Value);
    }

    [Fact]
    public async Task Handle_AuthenticatorOtpInvalidCode_ThrowsNotValidOtpCodeException()
    {
        var user = new FindByLoginResponse();
        user.User = new User { Id = 1, Username = "user" };

        _usersClient
            .Setup(c => c.FindByLoginAsync(It.IsAny<FindByLoginRequest>(), null, null, CancellationToken.None))
            .Returns(new AsyncUnaryCall<FindByLoginResponse>(
                Task.FromResult(user), Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => new Metadata(), () => { }));

        _context.AuthUserProperties.Add(new AuthUserProperty { UserId = 1, OtpEnabled = true, OtpSecret = Base32Encoding.ToString(KeyGeneration.GenerateRandomKey(20)) });
        _context.SaveChanges();

        var handler = CreateHandler();
        var command = new AuthCommand { Username = "user", Password = "pass", OtpCode = "000000" };

        await Assert.ThrowsAsync<NotValidOtpCodeException>(() => handler.Handle(command, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_EmailOtpValidCode_ProceedsToLogin()
    {
        var user = new FindByLoginResponse();
        user.User = new User { Id = 1, Username = "user" };

        _usersClient
            .Setup(c => c.FindByLoginAsync(It.IsAny<FindByLoginRequest>(), null, null, CancellationToken.None))
            .Returns(new AsyncUnaryCall<FindByLoginResponse>(
                Task.FromResult(user), Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => new Metadata(), () => { }));

        _usersClient
            .Setup(c => c.GetUserContactsAsync(It.IsAny<GetUserContactsRequest>(), null, null, CancellationToken.None))
            .Returns(new AsyncUnaryCall<GetUserContactsResponse>(
                Task.FromResult(new GetUserContactsResponse
                {
                    User = new User { Id = 1, Username = "user" },
                    Contact = new UserContact { Email = "test@test.com" }
                }), Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => new Metadata(), () => { }));

        _usersClient
            .Setup(c => c.RegisterDeviceAsync(It.IsAny<RegisterDeviceRequest>(), null, null, CancellationToken.None))
            .Returns(new AsyncUnaryCall<RegisterDeviceResponse>(
                Task.FromResult(new RegisterDeviceResponse()), Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => new Metadata(), () => { }));

        _context.UserPasswords.Add(new UserPassword { UserId = 1, PasswordHash = PasswordHasher.HashPassword("password123") });
        _context.AuthUserProperties.Add(new AuthUserProperty { UserId = 1, OtpEnabled = false, EmailOtpEnabled = true, LastEmailAuthCode = "123456" });
        _context.SaveChanges();

        _mediator.Setup(m => m.Send(It.IsAny<CreateTokenCommand>(), CancellationToken.None))
            .ReturnsAsync(new CreateTokenResponse
            {
                AccessToken = new Token { Value = "access", ExpirationDate = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.UtcNow.AddMinutes(30)) }
            });

        var handler = CreateHandler();
        var command = new AuthCommand { Username = "user", Password = "password123", OtpCode = "123456" };

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.NotNull(result);
        Assert.NotNull(result.RefreshToken);
        Assert.Equal("access", result.AccessToken.Value);
    }

    [Fact]
    public async Task Handle_EmailOtpInvalidCode_ThrowsNotValidOtpCodeException()
    {
        var user = new FindByLoginResponse();
        user.User = new User { Id = 1, Username = "user" };

        _usersClient
            .Setup(c => c.FindByLoginAsync(It.IsAny<FindByLoginRequest>(), null, null, CancellationToken.None))
            .Returns(new AsyncUnaryCall<FindByLoginResponse>(
                Task.FromResult(user), Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => new Metadata(), () => { }));

        _context.AuthUserProperties.Add(new AuthUserProperty { UserId = 1, OtpEnabled = false, EmailOtpEnabled = true, LastEmailAuthCode = "123456" });
        _context.SaveChanges();

        var handler = CreateHandler();
        var command = new AuthCommand { Username = "user", Password = "pass", OtpCode = "wrong" };

        await Assert.ThrowsAsync<NotValidOtpCodeException>(() => handler.Handle(command, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_RegisterDeviceFails_StillSucceeds()
    {
        var user = new FindByLoginResponse();
        user.User = new User { Id = 1, Username = "user" };

        _usersClient
            .Setup(c => c.FindByLoginAsync(It.IsAny<FindByLoginRequest>(), null, null, CancellationToken.None))
            .Returns(new AsyncUnaryCall<FindByLoginResponse>(
                Task.FromResult(user), Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => new Metadata(), () => { }));

        _usersClient
            .Setup(c => c.GetUserContactsAsync(It.IsAny<GetUserContactsRequest>(), null, null, CancellationToken.None))
            .Returns(new AsyncUnaryCall<GetUserContactsResponse>(
                Task.FromResult(new GetUserContactsResponse
                {
                    User = new User { Id = 1, Username = "user" },
                    Contact = new UserContact { Email = "test@test.com" }
                }), Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => new Metadata(), () => { }));

        _usersClient
            .Setup(c => c.RegisterDeviceAsync(It.IsAny<RegisterDeviceRequest>(), null, null, CancellationToken.None))
            .Throws(new Exception("Service unavailable"));

        _context.UserPasswords.Add(new UserPassword { UserId = 1, PasswordHash = PasswordHasher.HashPassword("password123") });
        _context.SaveChanges();

        _mediator.Setup(m => m.Send(It.IsAny<CreateTokenCommand>(), CancellationToken.None))
            .ReturnsAsync(new CreateTokenResponse
            {
                AccessToken = new Token { Value = "access", ExpirationDate = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.UtcNow.AddMinutes(30)) }
            });

        var handler = CreateHandler();
        var command = new AuthCommand { Username = "user", Password = "password123" };

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.NotNull(result);
        Assert.NotNull(result.RefreshToken);
        Assert.Equal("access", result.AccessToken.Value);
    }
}
