using System.Security.Cryptography;
using System.Text;

namespace BarkFluff.Settings.Settings;

public sealed record SettingsSetupOptions(bool Enabled, byte[] SecretHash)
{
    public static SettingsSetupOptions FromConfiguration(IConfiguration configuration)
    {
        var enabled = bool.TryParse(configuration["SETTINGS_SETUP_MODE"], out var parsed) && parsed;
        var secret = ReadSecret(configuration);
        if (enabled && string.IsNullOrWhiteSpace(secret))
            throw new InvalidOperationException("Settings setup mode requires SETTINGS_SETUP_SECRET_FILE or SETTINGS_SETUP_TOKEN.");

        return new SettingsSetupOptions(
            enabled,
            SHA256.HashData(Encoding.UTF8.GetBytes(secret.Trim())));
    }

    public bool IsValid(string? candidate)
    {
        if (!Enabled || string.IsNullOrWhiteSpace(candidate))
            return false;

        var candidateHash = SHA256.HashData(Encoding.UTF8.GetBytes(candidate.Trim()));
        return CryptographicOperations.FixedTimeEquals(SecretHash, candidateHash);
    }

    private static string ReadSecret(IConfiguration configuration)
    {
        var file = configuration["SETTINGS_SETUP_SECRET_FILE"];
        if (!string.IsNullOrWhiteSpace(file) && File.Exists(file))
            return File.ReadAllText(file);

        return configuration["SETTINGS_SETUP_TOKEN"] ?? string.Empty;
    }
}
