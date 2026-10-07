using System.Security.Cryptography;
using System.Text;
using BarkFluff.Identity.Settings;

namespace BarkFluff.Identity.Security;

public sealed class AuthenticationSecrets(JwtSettings settings)
{
    public static string NewSecret() => Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
    public string Hash(string value) => Convert.ToHexString(HMACSHA256.HashData(
        Encoding.UTF8.GetBytes(settings.SecretKey), Encoding.UTF8.GetBytes("authentication-challenge:" + value)));
    public bool Matches(string? hash, string value) => hash != null && CryptographicOperations.FixedTimeEquals(
        Encoding.UTF8.GetBytes(hash), Encoding.UTF8.GetBytes(Hash(value)));
}
