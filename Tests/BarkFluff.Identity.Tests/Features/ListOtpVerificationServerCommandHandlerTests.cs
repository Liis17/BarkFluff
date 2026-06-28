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
