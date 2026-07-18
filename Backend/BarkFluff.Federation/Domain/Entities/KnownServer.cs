using BarkFluff.Federation.Domain.Enums;

namespace BarkFluff.Federation.Domain.Entities;

public class KnownServer
{
    public string ServerName { get; set; } = string.Empty;

    public string FederationEndpoint { get; set; } = string.Empty;

    public string[] TlsSpkiSha256 { get; set; } = [];

    public KnownServerSource Source { get; set; }

    public KnownServerStatus Status { get; set; }

    public DateTime FirstSeenAt { get; set; }

    public DateTime LastSeenAt { get; set; }

    public DateTime? LastKeyRefreshAt { get; set; }

    public int ProtocolVersion { get; set; }

    public List<KnownServerKey> Keys { get; set; } = [];
}
