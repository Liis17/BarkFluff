using System.ComponentModel.DataAnnotations;

namespace BarkFluff.Users.Domain;

public class DevicePrekeyBundle
{
    [Key]
    public Guid DeviceId { get; set; }

    public UserDevice Device { get; set; }

    public long RegistrationId { get; set; }

    public byte[] IdentityPubkey { get; set; } = Array.Empty<byte>();

    public long SignedPrekeyId { get; set; }

    public byte[] SignedPrekeyPublic { get; set; } = Array.Empty<byte>();

    public byte[] SignedPrekeySignature { get; set; } = Array.Empty<byte>();

    public DateTime SignedPrekeyRotatedAt { get; set; }

    public DateTime CreatedAt { get; set; }
}
