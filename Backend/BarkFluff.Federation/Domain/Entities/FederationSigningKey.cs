namespace BarkFluff.Federation.Domain.Entities;

public class FederationSigningKey
{
    public string KeyId { get; set; } = string.Empty;

    public byte[] PublicKey { get; set; } = [];

    public byte[] PrivateKeySeed { get; set; } = [];

    public DateTime CreatedAt { get; set; }

    public DateTime? ExpiredAt { get; set; }

    public DateTime? RevokedAt { get; set; }
}
