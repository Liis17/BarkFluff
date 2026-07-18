using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Text.Json;

using BarkFluff.GrpcServer.Metrics;

using Org.Webpki.JsonCanonicalizer;

namespace BarkFluff.Federation.Services;

// Источник 1 discovery (docs/rearch/03-discovery.md): GET https://{servername}/.well-known/barkfluff,
// self-certifying (доверие — первому знакомству, канал защищён CA-TLS; никакого trust-all в проде).
public class WellKnownClient : IWellKnownClient
{
    private const int MaxResponseBytes = 64 * 1024;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private readonly ServernameValidator _validator;
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment;
    private readonly MetricsCollector _metrics;
    private readonly ILogger<WellKnownClient> _logger;

    public WellKnownClient(
        ServernameValidator validator,
        IConfiguration configuration,
        IHostEnvironment environment,
        MetricsCollector metrics,
        ILogger<WellKnownClient> logger)
    {
        _validator = validator;
        _configuration = configuration;
        _environment = environment;
        _metrics = metrics;
        _logger = logger;
    }

    public async Task<RemoteServerDocument?> FetchAsync(string servername, CancellationToken ct = default)
    {
        if (!ServernameValidator.TryNormalizeSyntax(servername, out var normalized))
            return null;

        var validatedIp = await _validator.ResolveAndValidateAsync(normalized, isManual: false, ct);
        if (validatedIp == null)
        {
            _logger.LogWarning("well-known: {Server} не резолвится в публичный IP (анти-SSRF)", normalized);
            return null;
        }

        // Dev-флаг: отключает CA-валидацию фетча ТОЛЬКО в Development (self-signed на стенде,
        // TLS/nginx — этап 1.6). В production игнорируется полностью.
        var allowInsecure = _environment.IsDevelopment() &&
            string.Equals(_configuration["Federation:Insecure:AllowUntrustedWellKnownTls"], "true", StringComparison.OrdinalIgnoreCase);

        if (allowInsecure)
            _logger.LogWarning("Federation:Insecure:AllowUntrustedWellKnownTls включён — CA-валидация well-known ОТКЛЮЧЕНА (Development)");

        using var handler = new SocketsHttpHandler
        {
            // Anti-rebinding: коннектимся строго по уже провалидированному IP, не по повторному резолву.
            ConnectCallback = async (context, cancellationToken) =>
            {
                var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                try
                {
                    await socket.ConnectAsync(validatedIp, context.DnsEndPoint.Port, cancellationToken);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            },
        };

        if (allowInsecure)
        {
            handler.SslOptions = new SslClientAuthenticationOptions
            {
                RemoteCertificateValidationCallback = (_, _, _, _) => true,
            };
        }

        using var httpClient = new HttpClient(handler) { Timeout = Timeout };

        string json;
        try
        {
            using var response = await httpClient.GetAsync($"https://{normalized}/.well-known/barkfluff", HttpCompletionOption.ResponseHeadersRead, ct);

            if (!response.IsSuccessStatusCode)
                return null;

            if (response.Content.Headers.ContentLength is > MaxResponseBytes)
                return null;

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var buffer = new MemoryStream();
            var chunk = new byte[8192];
            int read;
            while ((read = await stream.ReadAsync(chunk, ct)) > 0)
            {
                buffer.Write(chunk, 0, read);
                if (buffer.Length > MaxResponseBytes)
                    return null;
            }

            json = System.Text.Encoding.UTF8.GetString(buffer.ToArray());
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or SocketException)
        {
            _logger.LogInformation("well-known-фетч {Server} не удался: {Message}", normalized, ex.Message);
            return null;
        }

        return VerifyAndParse(json, normalized);
    }

    private RemoteServerDocument? VerifyAndParse(string json, string expectedServerName)
    {
        try
        {
            using var parsed = JsonDocument.Parse(json);
            var root = parsed.RootElement;

            var serverName = root.GetProperty("server_name").GetString();
            if (!string.Equals(serverName, expectedServerName, StringComparison.OrdinalIgnoreCase))
                return null;

            var signatureElement = root.GetProperty("signature");
            var keyId = signatureElement.GetProperty("key_id").GetString();
            var signatureValue = signatureElement.GetProperty("value").GetString();
            if (keyId == null || signatureValue == null)
                return null;

            var signingKeysElement = root.GetProperty("signing_keys");

            var keys = new List<RemoteSigningKey>();
            byte[]? signingPublicKey = null;
            foreach (var prop in signingKeysElement.EnumerateObject())
            {
                var publicKey = Convert.FromBase64String(prop.Value.GetProperty("key").GetString()!);
                DateTime? expiredAt = prop.Value.TryGetProperty("expired_at", out var expEl) && expEl.ValueKind == JsonValueKind.String
                    ? DateTime.Parse(expEl.GetString()!, null, System.Globalization.DateTimeStyles.RoundtripKind).ToUniversalTime()
                    : null;

                keys.Add(new RemoteSigningKey(prop.Name, publicKey, expiredAt));

                if (prop.Name == keyId)
                    signingPublicKey = publicKey;
            }

            if (signingPublicKey == null)
                return null;

            var withoutSignature = new Dictionary<string, JsonElement>();
            foreach (var prop in root.EnumerateObject())
            {
                if (prop.Name != "signature")
                    withoutSignature[prop.Name] = prop.Value;
            }

            var withoutSignatureJson = JsonSerializer.Serialize(withoutSignature);
            var canonicalBytes = new JsonCanonicalizer(withoutSignatureJson).GetEncodedUTF8();
            var signatureBytes = Convert.FromBase64String(signatureValue);

            if (!SigningKeyService.Verify(signingPublicKey, canonicalBytes, signatureBytes))
            {
                _metrics.Increment("wellknown_signature_failures");
                _logger.LogWarning("well-known {Server}: подпись документа не прошла проверку", expectedServerName);
                return null;
            }

            var federationElement = root.GetProperty("federation");
            var endpoint = federationElement.GetProperty("endpoint").GetString() ?? string.Empty;

            var tlsSpki = federationElement.TryGetProperty("tls_spki_sha256", out var spkiEl)
                ? spkiEl.EnumerateArray().Select(e => e.GetString() ?? string.Empty).ToArray()
                : [];

            var protocolVersions = federationElement.TryGetProperty("protocol_versions", out var pvEl)
                ? pvEl.EnumerateArray().Select(e => e.GetInt32()).ToArray()
                : [];

            return new RemoteServerDocument(serverName!, endpoint, tlsSpki, protocolVersions, keys);
        }
        catch (Exception ex) when (ex is JsonException or FormatException or KeyNotFoundException or InvalidOperationException)
        {
            _logger.LogWarning(ex, "well-known {Server}: документ не распарсен", expectedServerName);
            return null;
        }
    }
}
