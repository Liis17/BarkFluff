using BarkFluff.Node;
using BarkFluff.Status.Catalog;
using Microsoft.Extensions.Configuration;

namespace BarkFluff.Status.Configuration;

/// <summary>
/// Конфигурация статуса. Источники: NODE_DOMAIN и FEDERATION_SPKI_PIN (env из .env compose),
/// Status__ServiceOrigins__&lt;service&gt; (HTTPS origin вместо &lt;sub&gt;.&lt;NODE_DOMAIN&gt;),
/// Status__DisabledServices (через запятую: намеренно выключенные публичные компоненты),
/// Status__DatabasePath. Публичные запросы на конфигурацию не влияют.
/// </summary>
public sealed class StatusOptions
{
    public required string NodeDomain { get; init; }

    /// <summary>Итоговый HTTPS origin каждого включённого сервиса (без пути и завершающего '/').</summary>
    public required IReadOnlyDictionary<string, Uri> ServiceOrigins { get; init; }

    /// <summary>Base64(SHA256(SubjectPublicKeyInfo)) для Federation; null — обычная проверка сертификата.</summary>
    public string? FederationSpkiPin { get; init; }

    public required string DatabasePath { get; init; }

    public static StatusOptions From(IConfiguration configuration)
    {
        var domain = ParseNodeDomain(configuration["NODE_DOMAIN"]);
        var disabled = ParseDisabled(configuration["Status:DisabledServices"]);

        var overrides = configuration.GetSection("Status:ServiceOrigins").GetChildren()
            .ToDictionary(child => child.Key, child => child.Value, StringComparer.Ordinal);
        foreach (var key in overrides.Keys)
        {
            if (!PublicServices.All.Any(s => s.Key == key))
                throw new InvalidOperationException($"Status:ServiceOrigins содержит неизвестный сервис '{key}'.");
        }

        var origins = new Dictionary<string, Uri>(StringComparer.Ordinal);
        foreach (var service in PublicServices.All)
        {
            if (disabled.Contains(service.Key))
                continue;
            origins[service.Key] = overrides.TryGetValue(service.Key, out var value) && !string.IsNullOrWhiteSpace(value)
                ? ParseOrigin(service.Key, value)
                : new Uri($"https://{NodeSubdomains.Get(service.Key)}.{domain}");
        }

        if (origins.Count == 0)
            throw new InvalidOperationException("Нет включённых сервисов для проверки статуса.");

        return new StatusOptions
        {
            NodeDomain = domain,
            ServiceOrigins = origins,
            FederationSpkiPin = ParsePin(configuration["FEDERATION_SPKI_PIN"]),
            DatabasePath = string.IsNullOrWhiteSpace(configuration["Status:DatabasePath"])
                ? "data/status.db"
                : configuration["Status:DatabasePath"]!,
        };
    }

    private static string ParseNodeDomain(string? value)
    {
        var domain = value?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(domain))
            throw new InvalidOperationException("NODE_DOMAIN не задан.");
        if (!domain.Contains('.') || Uri.CheckHostName(domain) != UriHostNameType.Dns)
            throw new InvalidOperationException("NODE_DOMAIN должен быть DNS-именем без схемы, порта и пути.");
        return domain;
    }

    private static HashSet<string> ParseDisabled(string? value)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(value))
            return result;
        foreach (var raw in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!PublicServices.All.Any(s => s.Key == raw))
                throw new InvalidOperationException($"Status:DisabledServices содержит неизвестный сервис '{raw}'.");
            result.Add(raw);
        }
        return result;
    }

    private static Uri ParseOrigin(string service, string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment)
            || (uri.AbsolutePath != "/" && uri.AbsolutePath != string.Empty))
        {
            throw new InvalidOperationException(
                $"Status:ServiceOrigins:{service} должен быть HTTPS origin без пути, query и учётных данных.");
        }
        return new Uri(uri.GetLeftPart(UriPartial.Authority));
    }

    private static string? ParsePin(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(value.Trim());
        }
        catch (FormatException)
        {
            throw new InvalidOperationException("FEDERATION_SPKI_PIN должен быть Base64 SHA256 (32 байта).");
        }
        if (bytes.Length != 32)
            throw new InvalidOperationException("FEDERATION_SPKI_PIN должен быть Base64 SHA256 (32 байта).");
        return Convert.ToBase64String(bytes);
    }
}
