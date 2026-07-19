namespace BarkFluff.Federation.Services;

// Общая модель результата резолва одним из трёх источников (well-known/Navigator) —
// docs/rearch/03-discovery.md.
public record RemoteSigningKey(string KeyId, byte[] PublicKey, DateTime? ExpiredAt);

public record RemoteServerDocument(
    string ServerName,
    string FederationEndpoint,
    string[] TlsSpkiSha256,
    int[] ProtocolVersions,
    IReadOnlyList<RemoteSigningKey> SigningKeys,
    // Ключ, которым РЕАЛЬНО проверена подпись документа (well-known). Основа континуитета доверия
    // при ротации известного пира (P1-11): важен идентификатор+pubkey фактического подписанта,
    // а не присутствие ключа в списке signing_keys. Navigator-документ не подписан → оба null.
    string? SignedByKeyId = null,
    byte[]? SignedByPublicKey = null);
