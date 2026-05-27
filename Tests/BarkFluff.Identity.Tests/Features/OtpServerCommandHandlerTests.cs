using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Identity.Domain;
using BarkFluff.Identity.Features.ListOtpVerification;
using BarkFluff.Identity.Features.ListOtpVerificationServer;
using BarkFluff.Identity.Features.DisableOtpVerificationServer;
using BarkFluff.Identity.Persistence.Contexts;
using BarkFluff.Identity.Persistence.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Moq;

using Xunit;

namespace BarkFluff.Identity.Tests.Features;

public class ListOtpVerificationCommandHandlerTests
{
    private readonly UserContext _userContext;
    private readonly IdentityContext _context;
    private readonly AuthPropertiesStorage _authPropsStorage;
    private readonly Mock<ILogger<ListOtpVerificationCommandHandler>> _logger;

    public ListOtpVerificationCommandHandlerTests()
    {
        _userContext = TestHelper.CreateUserContext(1);
        _context = TestHelper.CreateContext();
        _authPropsStorage = new AuthPropertiesStorage(_context);
        _logger = new Mock<ILogger<ListOtpVerificationCommandHandler>>();
    }

    [Fact]
    public async Task Handle_NoAuthProperties_ThrowsOtpNotCreatedException()
    {
        var handler = new ListOtpVerificationCommandHandler(_userContext, _authPropsStorage, _logger.Object);
        var cmd = new ListOtpVerificationCommand();

        await Assert.ThrowsAsync<BarkFluff.Identity.Persistence.Exceptions.OtpNotCreatedException>(() => handler.Handle(cmd, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_AuthenticatorEnabled_ReturnsCorrectStatus()
    {
        _context.AuthUserProperties.Add(new AuthUserProperty { UserId = 1, OtpEnabled = true, EmailOtpEnabled = false });
        _context.SaveChanges();

        var handler = new ListOtpVerificationCommandHandler(_userContext, _authPropsStorage, _logger.Object);
        var cmd = new ListOtpVerificationCommand();

        var result = await handler.Handle(cmd, CancellationToken.None);

        Assert.True(result.AuthenticatorEnabled);
        Assert.False(result.EmailEnabled);
    }

    [Fact]
    public async Task Handle_EmailEnabled_ReturnsCorrectStatus()
    {
        _context.AuthUserProperties.Add(new AuthUserProperty { UserId = 1, OtpEnabled = false, EmailOtpEnabled = true });
        _context.SaveChanges();

        var handler = new ListOtpVerificationCommandHandler(_userContext, _authPropsStorage, _logger.Object);
        var cmd = new ListOtpVerificationCommand();

        var result = await handler.Handle(cmd, CancellationToken.None);

        Assert.False(result.AuthenticatorEnabled);
        Assert.True(result.EmailEnabled);
    }
}

public class ListOtpVerificationServerCommandHandlerTests
{
    private readonly IdentityContext _context;
    private readonly AuthPropertiesStorage _authPropsStorage;
    private readonly Mock<ILogger<ListOtpVerificationServerCommandHandler>> _logger;

    public ListOtpVerificationServerCommandHandlerTests()
    {
        _context = TestHelper.CreateContext();
        _authPropsStorage = new AuthPropertiesStorage(_context);
        _logger = new Mock<ILogger<ListOtpVerificationServerCommandHandler>>();
    }

    [Fact]
    public async Task Handle_NoAuthProperties_ReturnsAllFalse()
    {
        var handler = new ListOtpVerificationServerCommandHandler(_authPropsStorage, _logger.Object);
        var cmd = new ListOtpVerificationServerCommand { UserId = 1 };

        var result = await handler.Handle(cmd, CancellationToken.None);

        Assert.False(result.AuthenticatorEnabled);
        Assert.False(result.EmailEnabled);
    }

    [Fact]
    public async Task Handle_AuthenticatorEnabled_ReturnsCorrectStatus()
    {
        _context.AuthUserProperties.Add(new AuthUserProperty { UserId = 1, OtpEnabled = true, EmailOtpEnabled = false });
        _context.SaveChanges();

        var handler = new ListOtpVerificationServerCommandHandler(_authPropsStorage, _logger.Object);
        var cmd = new ListOtpVerificationServerCommand { UserId = 1 };

        var result = await handler.Handle(cmd, CancellationToken.None);

        Assert.True(result.AuthenticatorEnabled);
        Assert.False(result.EmailEnabled);
    }
}

public class DisableOtpVerificationServerCommandHandlerTests
{
    private readonly IdentityContext _context;
    private readonly AuthPropertiesStorage _authPropsStorage;
    private readonly Mock<ILogger<DisableOtpVerificationServerCommandHandler>> _logger;

    public DisableOtpVerificationServerCommandHandlerTests()
    {
        _context = TestHelper.CreateContext();
        _authPropsStorage = new AuthPropertiesStorage(_context);
        _logger = new Mock<ILogger<DisableOtpVerificationServerCommandHandler>>();
    }

    [Fact]
    public async Task Handle_AuthenticatorType_DisablesOtp()
    {
        _context.AuthUserProperties.Add(new AuthUserProperty { UserId = 1, OtpEnabled = true });
        _context.SaveChanges();

        var handler = new DisableOtpVerificationServerCommandHandler(_authPropsStorage, _logger.Object);
        var cmd = new DisableOtpVerificationServerCommand { UserId = 1, OtpType = BarkFluff.Proto.Identity.OtpTypeId.Authenticator };

        var result = await handler.Handle(cmd, CancellationToken.None);

        Assert.NotNull(result);
        var props = await _context.AuthUserProperties.FirstAsync(x => x.UserId == 1);
        Assert.False(props.OtpEnabled);
    }

    [Fact]
    public async Task Handle_EmailType_DisablesEmailOtp()
    {
        _context.AuthUserProperties.Add(new AuthUserProperty { UserId = 1, EmailOtpEnabled = true });
        _context.SaveChanges();

        var handler = new DisableOtpVerificationServerCommandHandler(_authPropsStorage, _logger.Object);
        var cmd = new DisableOtpVerificationServerCommand { UserId = 1, OtpType = BarkFluff.Proto.Identity.OtpTypeId.Email };

        var result = await handler.Handle(cmd, CancellationToken.None);

        Assert.NotNull(result);
        var props = await _context.AuthUserProperties.FirstAsync(x => x.UserId == 1);
        Assert.False(props.EmailOtpEnabled);
    }
}
