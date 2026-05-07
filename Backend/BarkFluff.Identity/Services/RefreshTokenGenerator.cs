using System.Security.Cryptography;

namespace BarkFluff.Identity.Services;

public static class RefreshTokenGenerator
{
    private const int TokenBytes = 32;

    public static string GenerateRefreshToken()
        => Convert.ToBase64String(RandomNumberGenerator.GetBytes(TokenBytes))
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
}
