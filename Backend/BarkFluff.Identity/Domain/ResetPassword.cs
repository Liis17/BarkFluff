using System.ComponentModel.DataAnnotations;

namespace BarkFluff.Identity.Domain;

public class ResetPassword
{
    [Key] public Guid Id { get; set; }

    public long UserId { get; set; }

    public DateTime CreatedAt { get; set; }

    public OtpType OtpType { get; set; }

    public string? OtpCode { get; set; }

    public bool IsApproved { get; set; }
}