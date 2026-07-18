using BarkFluff.Federation.Domain.Entities;
using BarkFluff.Federation.Domain.Enums;
using BarkFluff.Federation.Persistence.Contexts;
using BarkFluff.GrpcServer.Metrics;

using Microsoft.EntityFrameworkCore;

namespace BarkFluff.Federation.Services;

// Алгоритм резолва servername дословно по docs/rearch/03-discovery.md, "Алгоритм резолва".
public class ServerResolver
{
    private static readonly TimeSpan KeyRefreshInterval = TimeSpan.FromHours(24);

    private readonly FederationContext _context;
    private readonly IWellKnownClient _wellKnownClient;
    private readonly INavigatorClient _navigatorClient;
    private readonly MetricsCollector _metrics;
    private readonly ILogger<ServerResolver> _logger;

    public ServerResolver(
        FederationContext context,
        IWellKnownClient wellKnownClient,
        INavigatorClient navigatorClient,
        MetricsCollector metrics,
        ILogger<ServerResolver> logger)
    {
        _context = context;
        _wellKnownClient = wellKnownClient;
        _navigatorClient = navigatorClient;
        _metrics = metrics;
        _logger = logger;
    }

    public async Task<KnownServer?> ResolveAsync(string servername, CancellationToken ct = default)
    {
        if (!ServernameValidator.TryNormalizeSyntax(servername, out var normalized))
        {
            _metrics.Increment("discovery_failures");
            return null;
        }

        var existing = await _context.KnownServers
            .Include(s => s.Keys)
            .FirstOrDefaultAsync(s => s.ServerName == normalized, ct);

        if (existing != null && existing.Status == KnownServerStatus.Blocked)
            return null;

        if (existing != null && existing.Source == KnownServerSource.Manual)
        {
            _metrics.Increment("discovery_lookups.manual");
            return existing;
        }

        if (existing != null && existing.LastKeyRefreshAt != null && DateTime.UtcNow - existing.LastKeyRefreshAt < KeyRefreshInterval)
        {
            _metrics.Increment("discovery_lookups.cache");
            return existing;
        }

        var wellKnown = await _wellKnownClient.FetchAsync(normalized, ct);
        RemoteServerDocument? navigatorDoc = null;

        var isFirstContact = existing == null;

        if (wellKnown == null)
        {
            navigatorDoc = await _navigatorClient.GetServerByNameAsync(normalized, ct);
        }
        else if (isFirstContact)
        {
            // Кросс-сверка обязательна при первом контакте, когда доступны оба источника.
            navigatorDoc = await _navigatorClient.GetServerByNameAsync(normalized, ct);
            if (navigatorDoc != null && !KeysMatch(wellKnown, navigatorDoc))
            {
                _metrics.Increment("crosscheck_mismatches");
                _logger.LogWarning("Расхождение ключей well-known/Navigator при первом контакте с {Server}", normalized);
                return null;
            }
        }

        var chosen = wellKnown ?? navigatorDoc;
        var source = wellKnown != null ? KnownServerSource.WellKnown : KnownServerSource.Navigator;

        if (chosen == null)
        {
            _metrics.Increment("discovery_lookups." + (wellKnown != null ? "wellknown" : "navigator"));
            _metrics.Increment("discovery_failures");
            return null;
        }

        _metrics.Increment(source == KnownServerSource.WellKnown ? "discovery_lookups.wellknown" : "discovery_lookups.navigator");

        // Смена ключей у уже известной ноды — только если новый набор всё ещё включает
        // хотя бы один ранее доверенный ключ (цепочка доверия), иначе не обновляем запись.
        if (existing != null && !HasTrustedContinuity(existing, chosen))
        {
            _logger.LogWarning("Ключи {Server} сменились без доверенной цепочки — запись не обновлена", normalized);
            return existing.Status == KnownServerStatus.Blocked ? null : existing;
        }

        return await UpsertAsync(normalized, chosen, source, existing, ct);
    }

    private static bool KeysMatch(RemoteServerDocument a, RemoteServerDocument b)
    {
        var aKeys = a.SigningKeys.ToDictionary(k => k.KeyId, k => k.PublicKey);
        var bKeys = b.SigningKeys.ToDictionary(k => k.KeyId, k => k.PublicKey);

        if (aKeys.Count != bKeys.Count)
            return false;

        foreach (var (keyId, publicKey) in aKeys)
        {
            if (!bKeys.TryGetValue(keyId, out var otherKey) || !publicKey.AsSpan().SequenceEqual(otherKey))
                return false;
        }

        return true;
    }

    private static bool HasTrustedContinuity(KnownServer existing, RemoteServerDocument newDoc)
    {
        foreach (var oldKey in existing.Keys.Where(k => k.RevokedAt == null))
        {
            if (newDoc.SigningKeys.Any(newKey => newKey.KeyId == oldKey.KeyId && newKey.PublicKey.AsSpan().SequenceEqual(oldKey.PublicKey)))
                return true;
        }

        return false;
    }

    private async Task<KnownServer> UpsertAsync(string servername, RemoteServerDocument doc, KnownServerSource source, KnownServer? existing, CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        if (existing == null)
        {
            existing = new KnownServer
            {
                ServerName = servername,
                Source = source,
                Status = KnownServerStatus.Active,
                FirstSeenAt = now,
            };
            _context.KnownServers.Add(existing);
        }

        existing.FederationEndpoint = doc.FederationEndpoint;
        existing.TlsSpkiSha256 = doc.TlsSpkiSha256;
        existing.ProtocolVersion = doc.ProtocolVersions.Length > 0 ? doc.ProtocolVersions[0] : existing.ProtocolVersion;
        existing.LastSeenAt = now;
        existing.LastKeyRefreshAt = now;

        var existingKeyIds = existing.Keys.Select(k => k.KeyId).ToHashSet();
        foreach (var remoteKey in doc.SigningKeys)
        {
            if (existingKeyIds.Contains(remoteKey.KeyId))
                continue;

            existing.Keys.Add(new KnownServerKey
            {
                ServerName = servername,
                KeyId = remoteKey.KeyId,
                PublicKey = remoteKey.PublicKey,
                ExpiredAt = remoteKey.ExpiredAt,
            });
        }

        await _context.SaveChangesAsync(ct);
        return existing;
    }
}
