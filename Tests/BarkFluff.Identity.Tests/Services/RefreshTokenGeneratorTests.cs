using BarkFluff.Identity.Services;

using Xunit;

namespace BarkFluff.Identity.Tests.Services;

public class RefreshTokenGeneratorTests
{
    [Fact]
    public void GenerateRefreshToken_ReturnsNonEmptyString()
    {
        var token = RefreshTokenGenerator.GenerateRefreshToken();

        Assert.False(string.IsNullOrEmpty(token));
    }

    [Fact]
    public void GenerateRefreshToken_DoesNotContainPlusOrSlash()
    {
        var token = RefreshTokenGenerator.GenerateRefreshToken();

        Assert.DoesNotContain('+', token);
        Assert.DoesNotContain('/', token);
    }

    [Fact]
    public void GenerateRefreshToken_DoesNotContainPaddingEquals()
    {
        var token = RefreshTokenGenerator.GenerateRefreshToken();

        Assert.DoesNotContain('=', token);
    }

    [Fact]
    public void GenerateRefreshToken_CalledTwice_ProducesDifferentTokens()
    {
        var token1 = RefreshTokenGenerator.GenerateRefreshToken();
        var token2 = RefreshTokenGenerator.GenerateRefreshToken();

        Assert.NotEqual(token1, token2);
    }

    [Fact]
    public void GenerateRefreshToken_CalledMultipleTimes_AllUnique()
    {
        var tokens = new HashSet<string>();
        for (int i = 0; i < 100; i++)
        {
            tokens.Add(RefreshTokenGenerator.GenerateRefreshToken());
        }

        Assert.Equal(100, tokens.Count);
    }

    [Fact]
    public void GenerateRefreshToken_HasReasonableLength()
    {
        var token = RefreshTokenGenerator.GenerateRefreshToken();

        Assert.True(token.Length >= 30);
    }
}
