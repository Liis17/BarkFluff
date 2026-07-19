using BarkFluff.Federation.Domain.Entities;

namespace BarkFluff.Federation.Services;

// Единая reconciliation ключей пира из доверенного документа (P1-09). Раньше логика была
// продублирована в ServerResolver.UpsertAsync и FederationInternalApiService.UpsertManualPeer,
// и обе только ДОБАВЛЯЛИ неизвестные key_id — существующим не синхронизировался ExpiredAt,
// а исчезнувшие из документа ключи оставались пригодными.
//
// Политика (docs/rearch/03-discovery.md, "Политика обновления"):
// - новый key_id                          → добавить (pubkey + expired_at);
// - существующий key_id, тот же pubkey     → синхронизировать ExpiredAt (честная публикация истечения);
// - существующий key_id, ДРУГОЙ pubkey     → НЕ перезаписывать (аномалия переиспользования key_id — лог);
// - локальный key_id, отсутствующий в доке → отозвать (RevokedAt = now): пир перестал его публиковать.
public static class KnownServerKeyReconciler
{
    public static void Reconcile(
        KnownServer existing,
        IReadOnlyList<RemoteSigningKey> docKeys,
        DateTime now,
        ILogger? logger = null)
    {
        var docById = docKeys.ToDictionary(k => k.KeyId, k => k);

        foreach (var local in existing.Keys)
        {
            if (docById.TryGetValue(local.KeyId, out var doc))
            {
                if (local.PublicKey.AsSpan().SequenceEqual(doc.PublicKey))
                {
                    local.ExpiredAt = doc.ExpiredAt;
                }
                else
                {
                    logger?.LogWarning(
                        "Reconcile: key_id {KeyId} ноды {Server} переиспользован с другим pubkey — прежний ключ сохранён",
                        local.KeyId, existing.ServerName);
                }
            }
            else if (local.RevokedAt == null)
            {
                local.RevokedAt = now;
            }
        }

        var localIds = existing.Keys.Select(k => k.KeyId).ToHashSet();
        foreach (var doc in docKeys)
        {
            if (localIds.Contains(doc.KeyId))
                continue;

            existing.Keys.Add(new KnownServerKey
            {
                ServerName = existing.ServerName,
                KeyId = doc.KeyId,
                PublicKey = doc.PublicKey,
                ExpiredAt = doc.ExpiredAt,
            });
        }
    }
}
