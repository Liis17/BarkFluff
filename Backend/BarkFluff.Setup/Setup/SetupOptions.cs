using System.Security.Cryptography;
using System.Text;

namespace BarkFluff.Setup.Setup;

public sealed record SetupOptions(
    int Port,
    Uri SettingsUrl,
    string Secret,
    string? PublicOrigin,
    TimeSpan SessionLifetime)
{
    public static SetupOptions FromConfiguration(IConfiguration configuration)
    {
        var port = ParsePort(configuration["SETUP_PORT"] ?? configuration["RunSettings__Port"], 7032);
        var settingsUrl = ParseUri(configuration["SETTINGS_URL"] ?? "http://settings:7003", "SETTINGS_URL");
        var secret = ReadSecret(configuration);
        if (string.IsNullOrWhiteSpace(secret))
            throw new InvalidOperationException("Setup requires SETUP_SECRET_FILE or SETUP_TOKEN.");

        var publicOrigin = configuration["SETUP_PUBLIC_ORIGIN"];
        if (!string.IsNullOrWhiteSpace(publicOrigin))
            publicOrigin = NormalizeOrigin(publicOrigin, "SETUP_PUBLIC_ORIGIN");

        var lifetime = TimeSpan.FromSeconds(ParsePositiveInt(configuration["SETUP_SESSION_LIFETIME_SECONDS"], 7200));
        return new SetupOptions(port, settingsUrl, secret.Trim(), publicOrigin, lifetime);
    }

    public byte[] SecretHash => SHA256.HashData(Encoding.UTF8.GetBytes(Secret));

    private static string ReadSecret(IConfiguration configuration)
    {
        var file = configuration["SETUP_SECRET_FILE"];
        if (!string.IsNullOrWhiteSpace(file) && File.Exists(file))
            return File.ReadAllText(file);

        return configuration["SETUP_TOKEN"] ?? string.Empty;
    }

    private static int ParsePort(string? raw, int fallback)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return fallback;

        if (!int.TryParse(raw, out var port) || port is < 1 or > 65535)
            throw new InvalidOperationException("SETUP_PORT must be between 1 and 65535.");

        return port;
    }

    private static int ParsePositiveInt(string? raw, int fallback)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return fallback;

        if (!int.TryParse(raw, out var value) || value <= 0)
            throw new InvalidOperationException("SETUP_SESSION_LIFETIME_SECONDS must be a positive integer.");

        return value;
    }

    private static Uri ParseUri(string raw, string key)
    {
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri)
            || uri.UserInfo.Length > 0
            || !string.IsNullOrEmpty(uri.Fragment)
            || !string.IsNullOrEmpty(uri.Query))
            throw new InvalidOperationException($"{key} must be an absolute URL without userinfo, query or fragment.");

        return uri;
    }

    private static string NormalizeOrigin(string raw, string key)
    {
        var uri = ParseUri(raw.Trim().TrimEnd('/'), key);
        if (uri.AbsolutePath is not ("" or "/"))
            throw new InvalidOperationException($"{key} must contain only scheme, host and optional port.");

        return uri.GetLeftPart(UriPartial.Authority).TrimEnd('/');
    }
}
