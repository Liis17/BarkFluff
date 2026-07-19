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
    private readonly IS2SChannelInvalidator _channelInvalidator;
    private readonly MetricsCollector _metrics;
    private readonly ILogger<ServerResolver> _logger;

    public ServerResolver(
        FederationContext context,
        IWellKnownClient wellKnownClient,
        INavigatorClient navigatorClient,
        IS2SChannelInvalidator channelInvalidator,
        MetricsCollector metrics,
        ILogger<ServerResolver> logger)
    {
        _context = context;
        _wellKnownClient = wellKnownClient;
        _navigatorClient = navigatorClient;
        _channelInvalidator = channelInvalidator;
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

    // P1-12: manual-запись не перезатирается автоматическим discovery (ResolveAsync возвращает её мгновенно),
    // но плановую ротацию ключей у дружественной ноды подтягиваем безопасно — вызывается фоновым
    // PeerRefreshBackgroundService, не на hot-path. Обновление принимается ТОЛЬКО если новый well-known
    // подписан уже доверенным ключом (тот же континуитет, что у discovery-пиров, P1-11). Недоступный или
    // приватный (well-known с isManual=false отвергнет приватный servername) — запись остаётся как задал
    // админ; фиксируем попытку, чтобы уважать интервал refresh.
    public async Task RefreshManualPeerAsync(KnownServer existing, CancellationToken ct = default)
    {
        if (existing.Source != KnownServerSource.Manual || existing.Status == KnownServerStatus.Blocked)
            return;

        var due = existing.LastKeyRefreshAt == null || DateTime.UtcNow - existing.LastKeyRefreshAt >= KeyRefreshInterval;
        if (!due)
            return;

        var doc = await _wellKnownClient.FetchAsync(existing.ServerName, ct);
        if (doc != null && HasTrustedContinuity(existing, doc))
        {
            _metrics.Increment("manual_peer_refresh.applied");
            await UpsertAsync(existing.ServerName, doc, KnownServerSource.Manual, existing, ct);
            return;
        }

        _metrics.Increment("manual_peer_refresh.skipped");
        existing.LastKeyRefreshAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);
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
        // P1-11: континуитет доверия устанавливается ключом, который РЕАЛЬНО проверил подпись документа
        // (SignedByKeyId+pubkey), и только если этот ключ у нас уже доверенный (не отозван, не истёк).
        // Присутствия старого pubkey в списке signing_keys НЕдостаточно — атакующий может скопировать
        // чужой публичный ключ в список и подписать документ собственным ключом. Navigator-документ
        // не подписан (SignedByKeyId == null) → смена набора ключей через него не принимается.
        if (newDoc.SignedByKeyId == null || newDoc.SignedByPublicKey == null)
            return false;

        return existing.Keys.Any(k =>
            k.RevokedAt == null
            && (k.ExpiredAt == null || k.ExpiredAt > DateTime.UtcNow)
            && k.KeyId == newDoc.SignedByKeyId
            && k.PublicKey.AsSpan().SequenceEqual(newDoc.SignedByPublicKey));
    }

    private async Task<KnownServer> UpsertAsync(string servername, RemoteServerDocument doc, KnownServerSource source, KnownServer? existing, CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        var isNew = existing == null;
        var oldEndpoint = existing?.FederationEndpoint;
        var oldSpki = existing?.TlsSpkiSha256;

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

        // P1-09: единая reconciliation (добавить/синхронизировать/отозвать), а не только добавление.
        KnownServerKeyReconciler.Reconcile(existing, doc.SigningKeys, now, _logger);

        await _context.SaveChangesAsync(ct);

        // P1-08: смена endpoint/SPKI → сбросить кешированный S2S-канал (следующий вызов пересоберёт).
        if (!isNew && (oldEndpoint != doc.FederationEndpoint || !(oldSpki ?? []).SequenceEqual(doc.TlsSpkiSha256)))
            _channelInvalidator.Invalidate(servername);

        return existing;
    }
}
