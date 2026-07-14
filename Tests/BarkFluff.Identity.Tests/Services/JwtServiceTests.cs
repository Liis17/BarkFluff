using BarkFluff.Identity.Services;
using BarkFluff.Shared.Identity;

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

using Xunit;

namespace BarkFluff.Identity.Tests.Services;

public class JwtServiceTests
{
    private readonly JwtService _jwtService;
    private readonly BarkFluff.Identity.Settings.JwtSettings _jwtSettings;

    public JwtServiceTests()
    {
        _jwtSettings = new BarkFluff.Identity.Settings.JwtSettings
        {
            SecretKey = "test-secret-key-that-is-long-enough-for-hmac-sha256",
            Issuer = "BarkFluff",
            Audience = "BarkFluff",
            ExpiryMinutes = 30
        };
        _jwtService = new JwtService(_jwtSettings);
    }

    [Fact]
    public void GenerateUserToken_ReturnsValidJwt()
    {
        var token = _jwtService.GenerateUserToken(123, "device-1");

        Assert.False(string.IsNullOrEmpty(token.Value));
        Assert.True(token.ExpirationDate.Seconds > 0);
    }

    [Fact]
    public void GenerateUserToken_ContainsCorrectClaims()
    {
        var token = _jwtService.GenerateUserToken(456, "device-abc");

        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(token.Value);

        var userIdClaim = jwt.Claims.FirstOrDefault(c => c.Type == IdentityClaims.UserId);
        var tokenTypeClaim = jwt.Claims.FirstOrDefault(c => c.Type == IdentityClaims.TokenType);
        var deviceIdClaim = jwt.Claims.FirstOrDefault(c => c.Type == IdentityClaims.DeviceId);

        Assert.Equal("456", userIdClaim?.Value);
        Assert.Equal("User", tokenTypeClaim?.Value);
        Assert.Equal("device-abc", deviceIdClaim?.Value);
    }

    [Fact]
    public void GenerateUserToken_ContainsIssuerAndAudience()
    {
        var token = _jwtService.GenerateUserToken(1, "dev");

        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(token.Value);

        Assert.Equal(_jwtSettings.Issuer, jwt.Issuer);
        Assert.Equal(_jwtSettings.Audience, jwt.Audiences.First());
    }

    [Fact]
    public void GenerateUserToken_ExpirationMatchesSettings()
    {
        var before = DateTime.UtcNow.AddMinutes(_jwtSettings.ExpiryMinutes).AddSeconds(-1);
        var token = _jwtService.GenerateUserToken(1, "dev");
        var after = DateTime.UtcNow.AddMinutes(_jwtSettings.ExpiryMinutes).AddSeconds(1);

        var expiration = token.ExpirationDate.ToDateTime();

        Assert.True(expiration > before);
        Assert.True(expiration < after);
    }

    [Fact]
    public void GenerateServerToken_ReturnsValidJwt()
    {
        var token = _jwtService.GenerateServerToken(ServiceId.Identity);

        Assert.False(string.IsNullOrEmpty(token));
    }

    [Fact]
    public void GenerateServerToken_ContainsCorrectClaims()
    {
        var token = _jwtService.GenerateServerToken(ServiceId.Users);

        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(token);

        var serviceIdClaim = jwt.Claims.FirstOrDefault(c => c.Type == IdentityClaims.ServiceId);
        var tokenTypeClaim = jwt.Claims.FirstOrDefault(c => c.Type == IdentityClaims.TokenType);

        Assert.Equal("Users", serviceIdClaim?.Value);
        Assert.Equal("Service", tokenTypeClaim?.Value);
    }

    [Fact]
    public void GenerateServerToken_HasFarFutureExpiration()
    {
        var token = _jwtService.GenerateServerToken(ServiceId.Identity);

        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(token);

        Assert.True(jwt.ValidTo.Year >= 9999);
    }

    [Fact]
    public void GenerateBotToken_ContainsCorrectClaims()
    {
        var token = _jwtService.GenerateBotToken(789, "token-id-123");

        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(token);

        var userIdClaim = jwt.Claims.FirstOrDefault(c => c.Type == IdentityClaims.UserId);
        var tokenTypeClaim = jwt.Claims.FirstOrDefault(c => c.Type == IdentityClaims.TokenType);
        var botTokenIdClaim = jwt.Claims.FirstOrDefault(c => c.Type == IdentityClaims.BotTokenId);
        var deviceIdClaim = jwt.Claims.FirstOrDefault(c => c.Type == IdentityClaims.DeviceId);
        var serviceIdClaim = jwt.Claims.FirstOrDefault(c => c.Type == IdentityClaims.ServiceId);

        Assert.Equal("789", userIdClaim?.Value);
        Assert.Equal("Bot", tokenTypeClaim?.Value);
        Assert.Equal("token-id-123", botTokenIdClaim?.Value);
        Assert.Null(deviceIdClaim);
        Assert.Null(serviceIdClaim);
    }

    [Fact]
    public void GenerateBotToken_HasFarFutureExpiration()
    {
        var token = _jwtService.GenerateBotToken(1, "tid");

        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(token);

        Assert.True(jwt.ValidTo.Year >= 9999);
    }

    [Fact]
    public void GenerateUserToken_DifferentUsers_DifferentTokens()
    {
        var token1 = _jwtService.GenerateUserToken(1, "dev");
        var token2 = _jwtService.GenerateUserToken(2, "dev");

        Assert.NotEqual(token1.Value, token2.Value);
    }
}
