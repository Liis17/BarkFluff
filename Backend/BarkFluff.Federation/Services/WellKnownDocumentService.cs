using System.Text.Json;

using Org.Webpki.JsonCanonicalizer;

namespace BarkFluff.Federation.Services;

// /.well-known/barkfluff — схема и подпись: docs/rearch/03-discovery.md, "Источник 1".
// Документ пересобирается при старте и после ротации ключа, на GET отдаётся из кеша.
public class WellKnownDocumentService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;

    private volatile string? _cachedDocument;

    public WellKnownDocumentService(IServiceScopeFactory scopeFactory, IConfiguration configuration)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
    }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_configuration["Federation:ServerName"]) &&
        !string.IsNullOrWhiteSpace(_configuration["Federation:ExternalEndpoint"]);

    public string? GetCachedDocument() => _cachedDocument;

    public async Task RebuildAsync(CancellationToken ct = default)
    {
        if (!IsConfigured)
        {
            _cachedDocument = null;
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var keyService = scope.ServiceProvider.GetRequiredService<SigningKeyService>();

        var keys = await keyService.GetNonRevokedKeysAsync(ct);
        var activeKey = await keyService.GetActiveKeyAsync(ct);

        var spkiRaw = _configuration["Federation:TlsSpkiSha256"];
        var spki = string.IsNullOrWhiteSpace(spkiRaw)
            ? []
            : spkiRaw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var signingKeys = new Dictionary<string, object?>();
        foreach (var key in keys)
        {
            signingKeys[key.KeyId] = new Dictionary<string, object?>
            {
                ["key"] = Convert.ToBase64String(key.PublicKey),
                ["expired_at"] = key.ExpiredAt?.ToString("o"),
            };
        }

        // public_name: ServerProps:PublicName принадлежит Beacon (другой ServiceId, вне
        // конфиг-скоупа Federation) — пока пусто, кросс-сервисное чтение не входит в этап 1.2.
        var document = new Dictionary<string, object?>
        {
            ["server_name"] = _configuration["Federation:ServerName"],
            ["federation"] = new Dictionary<string, object?>
            {
                ["endpoint"] = _configuration["Federation:ExternalEndpoint"],
                ["tls_spki_sha256"] = spki,
                ["protocol_versions"] = new[] { 1 },
            },
            ["signing_keys"] = signingKeys,
            ["public_name"] = string.Empty,
        };

        var unsignedJson = JsonSerializer.Serialize(document);
        var canonicalBytes = new JsonCanonicalizer(unsignedJson).GetEncodedUTF8();
        var signature = keyService.Sign(activeKey, canonicalBytes);

        document["signature"] = new Dictionary<string, object?>
        {
            ["key_id"] = activeKey.KeyId,
            ["value"] = Convert.ToBase64String(signature),
        };

        _cachedDocument = JsonSerializer.Serialize(document);
    }
}
