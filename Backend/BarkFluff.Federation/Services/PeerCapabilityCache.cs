using System.Collections.Concurrent;

using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Proto.Federation;

namespace BarkFluff.Federation.Services;

/// <summary>
/// Какие возможности заявляет нода-партнёр в ответе <c>Ping</c> (этап 4.3).
/// </summary>
/// <remarks>
/// Ответ на риск «асимметрия ожиданий»: партнёр без capability <c>presence</c>/<c>typing</c>
/// всё равно отбросит наши вызовы, поэтому дешевле их не делать. Кеш in-memory с коротким TTL —
/// capability меняются вместе с версией ноды, персистентность не нужна.
/// Неудачный Ping трактуется как «не поддерживает» (fail-closed) и кешируется на короткий срок,
/// чтобы недоступный партнёр не порождал Ping на каждый цикл сверки.
/// </remarks>
public class PeerCapabilityCache
{
    private static readonly TimeSpan SuccessTtl = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan FailureTtl = TimeSpan.FromMinutes(1);

    private sealed record Entry(HashSet<string> Capabilities, DateTime ExpiresAt);

    private readonly ConcurrentDictionary<string, Entry> _cache = new();

    private readonly S2SChannelFactory _channelFactory;
    private readonly IConfiguration _configuration;
    private readonly MetricsCollector _metrics;
    private readonly ILogger<PeerCapabilityCache> _logger;

    public PeerCapabilityCache(
        S2SChannelFactory channelFactory,
        IConfiguration configuration,
        MetricsCollector metrics,
        ILogger<PeerCapabilityCache> logger)
    {
        _channelFactory = channelFactory;
        _configuration = configuration;
        _metrics = metrics;
        _logger = logger;
    }

    public async Task<bool> SupportsAsync(string serverName, string capability, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;

        if (_cache.TryGetValue(serverName, out var cached) && cached.ExpiresAt > now)
        {
            return cached.Capabilities.Contains(capability);
        }

        var capabilities = await FetchAsync(serverName, ct);

        _cache[serverName] = new Entry(
            capabilities ?? [],
            now + (capabilities is null ? FailureTtl : SuccessTtl));

        return capabilities?.Contains(capability) == true;
    }

    /// <summary>Сбросить кеш для ноды — например, после ручного обновления пира.</summary>
    public void Invalidate(string serverName) => _cache.TryRemove(serverName, out _);

    private async Task<HashSet<string>?> FetchAsync(string serverName, CancellationToken ct)
    {
        try
        {
            var invoker = await _channelFactory.GetInvokerAsync(serverName, ct);
            var client = new FederationS2SApi.FederationS2SApiClient(invoker);

            var response = await client.PingAsync(
                new PingRequest { OriginServer = _configuration["Federation:ServerName"] ?? string.Empty },
                cancellationToken: ct);

            return new HashSet<string>(response.Capabilities, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            _metrics.Increment("presence_peer_ping_errors");
            _logger.LogDebug(ex, "Ping ноды {Server} не удался — считаем capability неподдержанными", serverName);
            return null;
        }
    }
}
