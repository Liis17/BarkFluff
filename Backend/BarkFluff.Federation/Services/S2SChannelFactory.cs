using System.Collections.Concurrent;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

using BarkFluff.Federation.Persistence.Contexts;
using BarkFluff.GrpcServer.Metrics;

using Grpc.Core;
using Grpc.Core.Interceptors;
using Grpc.Net.Client;

using Microsoft.EntityFrameworkCore;

namespace BarkFluff.Federation.Services;

// Единственный путь исходящих S2S-запросов (docs/rearch/02-trust-and-certs.md, "Слой 1"):
// TLS c self-signed допустим — цепочка CA не проверяется, вместо неё SPKI-пиннинг публичного
// ключа TLS-сертификата пира. Канал кеширован per-destination, подписывающий интерсептор
// (XFedClientInterceptor) навешен один раз при создании.
// Discovery (KnownServers наполняется кодом) — этап 1.4; здесь пиры уже должны существовать
// в таблице (ручной SQL-сид на стенде).
public class S2SChannelFactory
{
    private readonly ConcurrentDictionary<string, CallInvoker> _invokers = new();

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ActiveSigningKeyCache _keyCache;
    private readonly MetricsCollector _metrics;
    private readonly ILogger<S2SChannelFactory> _logger;

    public S2SChannelFactory(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ActiveSigningKeyCache keyCache,
        MetricsCollector metrics,
        ILogger<S2SChannelFactory> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _keyCache = keyCache;
        _metrics = metrics;
        _logger = logger;
    }

    public async Task<CallInvoker> GetInvokerAsync(string serverName, CancellationToken ct = default)
    {
        if (_invokers.TryGetValue(serverName, out var cached))
            return cached;

        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<FederationContext>();
        var server = await context.KnownServers.FirstOrDefaultAsync(s => s.ServerName == serverName, ct);

        if (server == null)
            throw new InvalidOperationException($"Нода '{serverName}' не найдена в KnownServers (discovery — этап 1.4, на стенде — ручной SQL-сид)");

        var invoker = BuildInvoker(server.ServerName, server.FederationEndpoint, server.TlsSpkiSha256);
        return _invokers.GetOrAdd(serverName, invoker);
    }

    private CallInvoker BuildInvoker(string serverName, string endpoint, IReadOnlyCollection<string> tlsSpkiSha256)
    {
        var uri = new Uri(endpoint);
        var handler = new SocketsHttpHandler();

        if (uri.Scheme == "https")
        {
            handler.SslOptions = new SslClientAuthenticationOptions
            {
                RemoteCertificateValidationCallback = (_, certificate, _, _) =>
                {
                    if (tlsSpkiSha256.Count == 0)
                    {
                        _metrics.Increment("s2s_spki_pin_rejections");
                        _logger.LogWarning("SPKI-пин для {Server} пуст — TLS-соединение отклонено (fail-closed)", serverName);
                        return false;
                    }

                    if (certificate == null)
                        return false;

                    using var cert2 = new X509Certificate2(certificate);
                    var spki = cert2.PublicKey.ExportSubjectPublicKeyInfo();
                    var fingerprint = Convert.ToBase64String(SHA256.HashData(spki));

                    if (tlsSpkiSha256.Contains(fingerprint))
                        return true;

                    _metrics.Increment("s2s_spki_pin_rejections");
                    _logger.LogWarning("SPKI-пин ноды {Server} не совпал: получен {Fingerprint}", serverName, fingerprint);
                    return false;
                },
            };
        }
        // Plaintext (http://) — допускается только на стенде (без TLS/nginx, до этапа 1.6).

        var channel = GrpcChannel.ForAddress(endpoint, new GrpcChannelOptions { HttpHandler = handler });

        return channel.Intercept(new XFedClientInterceptor(_configuration, _keyCache, _metrics, serverName));
    }
}
