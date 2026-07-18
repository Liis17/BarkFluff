namespace BarkFluff.Federation.Services;

// Общая модель результата резолва одним из трёх источников (well-known/Navigator) —
// docs/rearch/03-discovery.md.
public record RemoteSigningKey(string KeyId, byte[] PublicKey, DateTime? ExpiredAt);

public record RemoteServerDocument(
    string ServerName,
    string FederationEndpoint,
    string[] TlsSpkiSha256,
    int[] ProtocolVersions,
    IReadOnlyList<RemoteSigningKey> SigningKeys);
