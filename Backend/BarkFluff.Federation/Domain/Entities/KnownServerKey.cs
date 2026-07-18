namespace BarkFluff.Federation.Domain.Entities;

public class KnownServerKey
{
    public string ServerName { get; set; } = string.Empty;

    public string KeyId { get; set; } = string.Empty;

    public byte[] PublicKey { get; set; } = [];

    public DateTime? ExpiredAt { get; set; }

    public DateTime? RevokedAt { get; set; }

    public KnownServer Server { get; set; } = null!;
}
