using System.ComponentModel.DataAnnotations;

namespace BarkFluff.Identity.Domain;

public class RefreshToken
{
    [Key]
    public long Id { get; set; }

    public string Value { get; set; }

    public long UserId { get; set; }

    public string DeviceId { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime ExpiresAt { get; set; }
}