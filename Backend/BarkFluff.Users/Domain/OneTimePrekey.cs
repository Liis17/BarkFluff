using System.ComponentModel.DataAnnotations;

namespace BarkFluff.Users.Domain;

public class OneTimePrekey
{
    [Key]
    public long Id { get; set; }

    public Guid DeviceId { get; set; }

    public UserDevice Device { get; set; }

    public long PrekeyId { get; set; }

    public byte[] PublicKey { get; set; } = Array.Empty<byte>();

    public DateTime CreatedAt { get; set; }
}
