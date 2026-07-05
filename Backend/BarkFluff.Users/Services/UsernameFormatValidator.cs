using System.Text.RegularExpressions;

namespace BarkFluff.Users.Services;

public static class UsernameFormatValidator
{
    private static readonly Regex UsernamePattern = new(@"^[a-zA-Z0-9_]{3,32}$", RegexOptions.Compiled);

    public static bool IsValid(string? username)
        => !string.IsNullOrWhiteSpace(username) && UsernamePattern.IsMatch(username);

    /// <summary>
    /// Заканчивается ли username на суффикс "bot" (регистронезависимо). Такие имена зарезервированы за ботами.
    /// </summary>
    public static bool HasBotSuffix(string? username)
        => !string.IsNullOrWhiteSpace(username) && username.TrimEnd().EndsWith("bot", StringComparison.OrdinalIgnoreCase);
}
