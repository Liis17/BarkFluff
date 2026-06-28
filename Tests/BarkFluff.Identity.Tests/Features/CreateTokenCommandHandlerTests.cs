using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Identity.Features.CreateToken;
using BarkFluff.Identity.Persistence.Contexts;
using BarkFluff.Identity.Persistence.Services;
using BarkFluff.Identity.Services;
using BarkFluff.Shared.Exceptions.Identity;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Moq;

using Xunit;

namespace BarkFluff.Identity.Tests.Features;

public class CreateTokenCommandHandlerTests
{
    private readonly IdentityContext _context;
    private readonly RefreshTokensStorage _refreshTokensStorage;
    private readonly JwtService _jwtService;
    private readonly MetricsCollector _metrics;
    private readonly Mock<ILogger<CreateTokenCommandHandler>> _logger;

    public CreateTokenCommandHandlerTests()
    {
        _context = TestHelper.CreateContext();
        _refreshTokensStorage = new RefreshTokensStorage(_context);
        _jwtService = TestHelper.CreateJwtService();
        _metrics = new MetricsCollector();
        _logger = new Mock<ILogger<CreateTokenCommandHandler>>();
    }

    private CreateTokenCommandHandler CreateHandler()
    {
        return new CreateTokenCommandHandler(
            _refreshTokensStorage, _jwtService, _metrics, _logger.Object);
    }

    [Fact]
    public async Task Handle_TokenNotFound_ThrowsInvalidRefreshTokenException()
    {
        var handler = CreateHandler();
        var cmd = new CreateTokenCommand { RefreshToken = "invalid" };

        await Assert.ThrowsAsync<InvalidRefreshTokenException>(() => handler.Handle(cmd, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_ExpiredToken_ThrowsInvalidRefreshTokenException()
    {
        _context.RefreshTokens.Add(new Domain.RefreshToken
        {
            Value = "expired", UserId = 1, DeviceId = "dev1",
            ExpiresAt = DateTime.UtcNow.AddDays(-1)
        });
        _context.SaveChanges();

        var handler = CreateHandler();
        var cmd = new CreateTokenCommand { RefreshToken = "expired" };

        await Assert.ThrowsAsync<InvalidRefreshTokenException>(() => handler.Handle(cmd, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_EmptyDeviceId_ThrowsInvalidRefreshTokenException()
    {
        _context.RefreshTokens.Add(new Domain.RefreshToken
        {
            Value = "noDevice", UserId = 1, DeviceId = "",
            ExpiresAt = DateTime.UtcNow.AddDays(1)
        });
        _context.SaveChanges();

        var handler = CreateHandler();
        var cmd = new CreateTokenCommand { RefreshToken = "noDevice" };

        await Assert.ThrowsAsync<InvalidRefreshTokenException>(() => handler.Handle(cmd, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_ValidToken_ReturnsAccessToken()
    {
        _context.RefreshTokens.Add(new Domain.RefreshToken
        {
            Value = "valid", UserId = 42, DeviceId = "dev1",
            ExpiresAt = DateTime.UtcNow.AddDays(1)
        });
        _context.SaveChanges();

        var handler = CreateHandler();
        var cmd = new CreateTokenCommand { RefreshToken = "valid" };

        var result = await handler.Handle(cmd, CancellationToken.None);

        Assert.NotNull(result);
        Assert.False(string.IsNullOrEmpty(result.AccessToken.Value));
        Assert.Equal(1, _metrics.SnapshotAndReset().GetValueOrDefault("tokens_refreshed"));
    }
}
