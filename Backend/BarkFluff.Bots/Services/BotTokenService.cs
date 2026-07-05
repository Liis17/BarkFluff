using System.Security.Cryptography;
using System.Text;

namespace BarkFluff.Bots.Services;

/// <summary>
/// Токены ботов формата {botId}:{secret}, secret = 32 случайных байта base64url.
/// В БД хранится только SHA-256 хеш секрета; сравнение — constant-time.
/// </summary>
public class BotTokenService
{
    private const int SecretLengthBytes = 32;

    /// <summary>Сгенерировать новый токен. Возвращает (plaintext-токен, SHA-256 хеш для БД).</summary>
    public (string Token, string TokenHash) GenerateToken(long botId)
    {
        var secretBytes = RandomNumberGenerator.GetBytes(SecretLengthBytes);
        var secret = Base64UrlEncode(secretBytes);
        return ($"{botId}:{secret}", HashSecret(secret));
    }

    /// <summary>Разобрать токен на botId и секрет. false = невалидный формат.</summary>
    public bool TryParseToken(string? token, out long botId, out string secret)
    {
        botId = 0;
        secret = string.Empty;

        if (string.IsNullOrWhiteSpace(token))
            return false;

        var separatorIndex = token.IndexOf(':');
        if (separatorIndex <= 0 || separatorIndex == token.Length - 1)
            return false;

        if (!long.TryParse(token.AsSpan(0, separatorIndex), out botId) || botId <= 0)
            return false;

        secret = token[(separatorIndex + 1)..];
        return true;
    }

    /// <summary>Проверить секрет против хеша из БД (constant-time).</summary>
    public bool VerifySecret(string secret, string tokenHash)
    {
        var actualHash = Encoding.UTF8.GetBytes(HashSecret(secret));
        var expectedHash = Encoding.UTF8.GetBytes(tokenHash);
        return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
    }

    private static string HashSecret(string secret)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret))).ToLowerInvariant();

    private static string Base64UrlEncode(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
