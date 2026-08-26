using BarkFluff.GrpcServer.Metrics;
using BarkFluff.GrpcServer.Tracker;
using BarkFluff.Identity.Domain;
using DomainOtpType = BarkFluff.Identity.Domain.OtpType;
using BarkFluff.Identity.Features.ConfirmResetPassword;
using BarkFluff.Identity.Features.CreateToken;
using BarkFluff.Identity.Infrastructure;
using BarkFluff.Identity.Persistence.Contexts;
using BarkFluff.Identity.Persistence.Services;
using BarkFluff.Identity.Security;
using BarkFluff.Identity.Services;
using BarkFluff.Shared.Exceptions.Identity;

using MediatR;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Moq;

using OtpNet;

using Xunit;

namespace BarkFluff.Identity.Tests.Features;

public class ConfirmResetPasswordCommandHandlerTests
{
    private readonly ResetPasswordsStorage _resetPasswordsStorage;
    private readonly AuthPropertiesStorage _authPropsStorage;
    private readonly PasswordsStorage _passwordsStorage;
    private readonly RefreshTokensStorage _refreshTokensStorage;
    private readonly Mock<IMediator> _mediator;
    private readonly RequestContext _requestContext;
    private readonly MetricsCollector _metrics;
    private readonly Mock<ILogger<ConfirmResetPasswordCommandHandler>> _logger;
    private readonly IdentityContext _context;

    public ConfirmResetPasswordCommandHandlerTests()
    {
        _context = TestHelper.CreateContext();
        _resetPasswordsStorage = new ResetPasswordsStorage(_context);
        _authPropsStorage = new AuthPropertiesStorage(_context);
        _passwordsStorage = new PasswordsStorage(_context);
        _refreshTokensStorage = new RefreshTokensStorage(_context);
        _mediator = new Mock<IMediator>();
        _requestContext = new RequestContext
        {
            DeviceName = "Dev", OperationSystem = "Win", AppName = "BF", AppVersion = "1.0", DeviceId = "dev1"
        };
        _metrics = new MetricsCollector();
        _logger = new Mock<ILogger<ConfirmResetPasswordCommandHandler>>();
    }

    private static RequestContext BuildRequestContext(
        string? deviceName = "Dev",
        string? os = "Win",
        string? appName = "BF",
        string? appVersion = "1.0",
        string? deviceId = "dev1") => new()
    {
        DeviceName = deviceName,
        OperationSystem = os,
        AppName = appName,
        AppVersion = appVersion,
        DeviceId = deviceId
    };

    private ConfirmResetPasswordCommandHandler CreateHandler(
        RequestContext? ctx = null,
        TestHelper.TestIdentityAbuseGuard? abuseGuard = null)
    {
        return new ConfirmResetPasswordCommandHandler(
            _resetPasswordsStorage, _authPropsStorage, _passwordsStorage,
            _refreshTokensStorage, _mediator.Object, ctx ?? _requestContext, _metrics, _logger.Object,
            abuseGuard ?? TestHelper.CreateAbuseGuard());
    }

    [Fact]
    public async Task Handle_NoDeviceName_ThrowsXDeviceNameIsRequiredException()
    {
        var handler = CreateHandler(BuildRequestContext(deviceName: null));
        var cmd = new ConfirmResetPasswordCommand { ResetId = Guid.NewGuid(), OtpCode = "123456" };

        await Assert.ThrowsAsync<XDeviceNameIsRequiredException>(() => handler.Handle(cmd, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_ResetNotFound_ThrowsResetIdNotFoundException()
    {
        var handler = CreateHandler();
        var cmd = new ConfirmResetPasswordCommand { ResetId = Guid.NewGuid(), OtpCode = "123456" };

        await Assert.ThrowsAsync<ResetIdNotFoundException>(() => handler.Handle(cmd, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_AlreadyApproved_ThrowsResetIdHasIsApprovedException()
    {
        _context.ResetPasswords.Add(new ResetPassword { IsApproved = true, ExpiresAt = DateTime.UtcNow.AddMinutes(10) });
        _context.SaveChanges();

        var handler = CreateHandler();
        var cmd = new ConfirmResetPasswordCommand { ResetId = _context.ResetPasswords.First().Id, OtpCode = "123456" };

        await Assert.ThrowsAsync<ResetIdHasIsApprovedException>(() => handler.Handle(cmd, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_Expired_ThrowsResetIdExpiredException()
    {
        _context.ResetPasswords.Add(new ResetPassword { IsApproved = false, ExpiresAt = DateTime.UtcNow.AddMinutes(-5) });
        _context.SaveChanges();

        var handler = CreateHandler();
        var cmd = new ConfirmResetPasswordCommand { ResetId = _context.ResetPasswords.First().Id, OtpCode = "123456" };

        await Assert.ThrowsAsync<ResetIdExpiredException>(() => handler.Handle(cmd, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_EmailOtpWrongCode_ThrowsNotValidOtpCodeException()
    {
        var resetId = Guid.NewGuid();
        _context.ResetPasswords.Add(new ResetPassword
        {
            Id = resetId,
            IsApproved = false,
            ExpiresAt = DateTime.UtcNow.AddMinutes(5),
            OtpType = DomainOtpType.Email,
            OtpCode = "123456",
            UserId = 1
        });
        _context.SaveChanges();

        var handler = CreateHandler();
        var cmd = new ConfirmResetPasswordCommand { ResetId = resetId, OtpCode = "654321" };

        await Assert.ThrowsAsync<NotValidOtpCodeException>(() => handler.Handle(cmd, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_FifthWrongOtp_InvalidatesResetRequest()
    {
        var resetId = Guid.NewGuid();
        _context.ResetPasswords.Add(new ResetPassword
        {
            Id = resetId,
            IsApproved = false,
            ExpiresAt = DateTime.UtcNow.AddMinutes(5),
            OtpType = DomainOtpType.Email,
            OtpCode = "123456",
            UserId = 1
        });
        _context.SaveChanges();

        var abuseGuard = TestHelper.CreateAbuseGuard();
        abuseGuard.CodeFailureResult = new IdentityFailureResult(5, true);
        var handler = CreateHandler(abuseGuard: abuseGuard);

        await Assert.ThrowsAsync<IdentityLockoutException>(() => handler.Handle(
            new ConfirmResetPasswordCommand { ResetId = resetId, OtpCode = "654321" },
            CancellationToken.None));

        var reset = await _context.ResetPasswords.FindAsync(resetId);
        Assert.True(reset!.IsApproved);
        Assert.Null(reset.OtpCode);
        _mediator.Verify(m => m.Send(It.IsAny<CreateTokenCommand>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal(1, abuseGuard.CodeFailureCalls);
    }

    [Fact]
    public async Task Handle_MissingOtp_DoesNotRegisterFailure()
    {
        var resetId = Guid.NewGuid();
        _context.ResetPasswords.Add(new ResetPassword
        {
            Id = resetId,
            IsApproved = false,
            ExpiresAt = DateTime.UtcNow.AddMinutes(5),
            OtpType = DomainOtpType.Email,
            OtpCode = "123456",
            UserId = 1
        });
        _context.SaveChanges();

        var abuseGuard = TestHelper.CreateAbuseGuard();
        var handler = CreateHandler(abuseGuard: abuseGuard);

        await Assert.ThrowsAsync<OtpCodeNeedException>(() => handler.Handle(
            new ConfirmResetPasswordCommand { ResetId = resetId },
            CancellationToken.None));

        Assert.Equal(0, abuseGuard.CodeFailureCalls);
    }

    [Fact]
    public async Task Handle_EmailOtpValidCode_Succeeds()
    {
        var resetId = Guid.NewGuid();
        _context.ResetPasswords.Add(new ResetPassword
        {
            Id = resetId,
            IsApproved = false,
            ExpiresAt = DateTime.UtcNow.AddMinutes(5),
            OtpType = DomainOtpType.Email,
            OtpCode = "123456",
            UserId = 1
        });
        _context.UserPasswords.Add(new UserPassword { UserId = 1, PasswordHash = "oldhash" });
        _context.SaveChanges();

        _mediator.Setup(m => m.Send(It.IsAny<CreateTokenCommand>(), CancellationToken.None))
            .ReturnsAsync(new BarkFluff.Proto.Identity.CreateTokenResponse
            {
                AccessToken = new BarkFluff.Proto.Identity.Token { Value = "at" }
            });

        var handler = CreateHandler();
        var cmd = new ConfirmResetPasswordCommand { ResetId = resetId, OtpCode = "123456" };

        var result = await handler.Handle(cmd, CancellationToken.None);

        Assert.NotNull(result);
        Assert.NotNull(result.RefreshToken);
        var reset = await _context.ResetPasswords.FindAsync(resetId);
        Assert.True(reset!.IsApproved);
        var pw = await _context.UserPasswords.FirstOrDefaultAsync(x => x.UserId == 1);
        Assert.Null(pw!.PasswordHash);
    }

    [Fact]
    public async Task Handle_AuthenticatorOtpValidCode_Succeeds()
    {
        var key = KeyGeneration.GenerateRandomKey(20);
        var base32 = Base32Encoding.ToString(key);
        var totp = new Totp(key);
        var validCode = totp.ComputeTotp();
        var resetId = Guid.NewGuid();

        _context.ResetPasswords.Add(new ResetPassword
        {
            Id = resetId,
            IsApproved = false,
            ExpiresAt = DateTime.UtcNow.AddMinutes(10),
            OtpType = DomainOtpType.Authenticator,
            UserId = 1
        });
        _context.AuthUserProperties.Add(new AuthUserProperty { UserId = 1, OtpSecret = base32 });
        _context.SaveChanges();

        _mediator.Setup(m => m.Send(It.IsAny<CreateTokenCommand>(), CancellationToken.None))
            .ReturnsAsync(new BarkFluff.Proto.Identity.CreateTokenResponse
            {
                AccessToken = new BarkFluff.Proto.Identity.Token { Value = "at" }
            });

        var handler = CreateHandler();
        var cmd = new ConfirmResetPasswordCommand { ResetId = resetId, OtpCode = validCode };

        var result = await handler.Handle(cmd, CancellationToken.None);

        Assert.NotNull(result);
        var reset = await _context.ResetPasswords.FindAsync(resetId);
        Assert.True(reset!.IsApproved);
    }

    [Fact]
    public async Task Handle_FallbackDeviceId_WhenDeviceIdIsNull()
    {
        var resetId = Guid.NewGuid();

        _context.ResetPasswords.Add(new ResetPassword
        {
            Id = resetId,
            IsApproved = false,
            ExpiresAt = DateTime.UtcNow.AddMinutes(5),
            OtpType = DomainOtpType.Email,
            OtpCode = "123456",
            UserId = 1
        });
        _context.SaveChanges();

        _mediator.Setup(m => m.Send(It.IsAny<CreateTokenCommand>(), CancellationToken.None))
            .ReturnsAsync(new BarkFluff.Proto.Identity.CreateTokenResponse { AccessToken = new BarkFluff.Proto.Identity.Token() });

        var handler = CreateHandler(BuildRequestContext(deviceId: null));
        var cmd = new ConfirmResetPasswordCommand { ResetId = resetId, OtpCode = "123456" };

        var result = await handler.Handle(cmd, CancellationToken.None);

        Assert.NotNull(result);
    }

    [Fact]
    public async Task Handle_NoOs_ThrowsXOsNameIsRequiredException()
    {
        var handler = CreateHandler(BuildRequestContext(os: null));
        var cmd = new ConfirmResetPasswordCommand { ResetId = Guid.NewGuid(), OtpCode = "123456" };

        await Assert.ThrowsAsync<XOsNameIsRequiredException>(() => handler.Handle(cmd, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_NoAppInfo_ThrowsXAppInfoIsRequiedException()
    {
        var handler = CreateHandler(BuildRequestContext(appName: null));
        var cmd = new ConfirmResetPasswordCommand { ResetId = Guid.NewGuid(), OtpCode = "123456" };

        await Assert.ThrowsAsync<XAppInfoIsRequiedException>(() => handler.Handle(cmd, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_AuthenticatorOtpInvalidCode_ThrowsNotValidOtpCodeException()
    {
        var resetId = Guid.NewGuid();

        _context.ResetPasswords.Add(new ResetPassword
        {
            Id = resetId,
            IsApproved = false,
            ExpiresAt = DateTime.UtcNow.AddMinutes(10),
            OtpType = DomainOtpType.Authenticator,
            UserId = 1
        });
        _context.AuthUserProperties.Add(new AuthUserProperty { UserId = 1, OtpSecret = Base32Encoding.ToString(KeyGeneration.GenerateRandomKey(20)) });
        _context.SaveChanges();

        var handler = CreateHandler();
        var cmd = new ConfirmResetPasswordCommand { ResetId = resetId, OtpCode = "000000" };

        await Assert.ThrowsAsync<NotValidOtpCodeException>(() => handler.Handle(cmd, CancellationToken.None));
    }
}
