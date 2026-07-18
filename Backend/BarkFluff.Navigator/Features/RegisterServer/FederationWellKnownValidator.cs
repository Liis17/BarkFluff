using System.Text.Json;

namespace BarkFluff.Navigator.Features.RegisterServer;

// docs/rearch/phase-1/step-1.5-navigator-persistence.md, Изменение 4: регистрация с server_name
// принимается только после проверки /.well-known/barkfluff заявленного домена — доказуемое
// владение доменом. Без проверки Ed25519-подписи документа (это не входит в скоуп Navigator —
// сверяются только байты ключей по key_id; подпись проверяют сами ноды при XFed/discovery).
public class FederationWellKnownValidator
{
    private const int MaxBytes = 64 * 1024;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private readonly IHostEnvironment _environment;
    private readonly ILogger<FederationWellKnownValidator> _logger;

    public FederationWellKnownValidator(IHostEnvironment environment, ILogger<FederationWellKnownValidator> logger)
    {
        _environment = environment;
        _logger = logger;
    }

    public virtual async Task<bool> ValidateAsync(string normalizedServerName, IReadOnlyList<(string KeyId, byte[] PublicKey)> claimedKeys, CancellationToken ct)
    {
        if (!await FederationServernameGuard.ResolvesToPublicAddressAsync(normalizedServerName, ct))
            return false;

        // Dev-флаг для стенда (self-signed до этапа 1.6) — только вне production, по аналогии
        // с Federation:Insecure:AllowUntrustedWellKnownTls.
        var insecure = !_environment.IsProduction() &&
            string.Equals(Environment.GetEnvironmentVariable("NAVIGATOR_INSECURE_WELLKNOWN"), "1", StringComparison.Ordinal);

        using var handler = new HttpClientHandler();
        if (insecure)
        {
            handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
            _logger.LogWarning("NAVIGATOR_INSECURE_WELLKNOWN=1 — CA-валидация well-known отключена (не production)");
        }

        using var client = new HttpClient(handler) { Timeout = Timeout };

        string json;
        try
        {
            using var response = await client.GetAsync($"https://{normalizedServerName}/.well-known/barkfluff", ct);

            if (!response.IsSuccessStatusCode)
                return false;

            if (response.Content.Headers.ContentLength is > MaxBytes)
                return false;

            json = await response.Content.ReadAsStringAsync(ct);
            if (json.Length > MaxBytes)
                return false;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogInformation("well-known-фетч {Server} не удался: {Message}", normalizedServerName, ex.Message);
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var docServerName = root.GetProperty("server_name").GetString();
            if (!string.Equals(docServerName, normalizedServerName, StringComparison.OrdinalIgnoreCase))
                return false;

            var docKeys = new Dictionary<string, byte[]>();
            foreach (var prop in root.GetProperty("signing_keys").EnumerateObject())
            {
                docKeys[prop.Name] = Convert.FromBase64String(prop.Value.GetProperty("key").GetString()!);
            }

            foreach (var (keyId, publicKey) in claimedKeys)
            {
                if (!docKeys.TryGetValue(keyId, out var docKey) || !docKey.AsSpan().SequenceEqual(publicKey))
                    return false;
            }

            return true;
        }
        catch (Exception ex) when (ex is JsonException or FormatException or KeyNotFoundException or InvalidOperationException)
        {
            _logger.LogWarning(ex, "well-known {Server}: документ не распарсен", normalizedServerName);
            return false;
        }
    }
}
