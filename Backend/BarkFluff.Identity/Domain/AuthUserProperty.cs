using System.ComponentModel.DataAnnotations;

namespace BarkFluff.Identity.Domain;

public class AuthUserProperty
{
    [Key]
    public long Id { get; set; }

    public long UserId { get; set; }

    public bool OtpEnabled { get; set; }

    public bool EmailOtpEnabled { get; set; }

    public string? OtpSecret { get; set; }

    public OtpType SelectedOtpType { get; set; }

    public string? LastEmailAuthCode { get; set; }
}