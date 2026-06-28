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
