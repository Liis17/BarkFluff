using System.Security.Cryptography;
using System.Text;

namespace BarkFluff.Identity.Services;

public static class PasswordHasher
{
    private const int BCryptWorkFactor = 12;

    public static string HashPassword(string password)
        => BCrypt.Net.BCrypt.HashPassword(password, BCryptWorkFactor);

    public static bool VerifyPassword(string password, string? storedHash)
    {
        if (string.IsNullOrEmpty(storedHash))
            return false;

        if (IsBCryptHash(storedHash))
            return BCrypt.Net.BCrypt.Verify(password, storedHash);

        // Legacy SHA-256 без соли — поддержка существующих паролей.
        // Why: миграционная стратегия "только BCrypt для новых паролей",
        // старые хеши проверяются прежним алгоритмом до смены пароля.
        var legacyHash = ComputeLegacySha256(password);
        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(legacyHash),
            Encoding.ASCII.GetBytes(storedHash));
    }

    private static bool IsBCryptHash(string hash)
        => hash.Length >= 4 && hash[0] == '$' && hash[1] == '2';

    private static string ComputeLegacySha256(string password)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(password));
        return Convert.ToBase64String(bytes);
    }
}
