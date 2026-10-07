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

    public DateTime? LastEmailAuthCodeExpiresAt { get; set; }
    public BarkFluff.Proto.Identity.AuthLoginMode LoginMode { get; set; }
    public OtpType PreferredFactor { get; set; }
    public long? TelegramId { get; set; }
    public string? TelegramUsername { get; set; }
    public bool TelegramEnabled { get; set; }
    public bool TelegramOtpEnabled { get; set; }
    public bool FastAuthTelegramEnabled { get; set; }
    public BarkFluff.Proto.Identity.LoginNotificationChannel NotificationChannel { get; set; } =
        BarkFluff.Proto.Identity.LoginNotificationChannel.Email;
    public int PolicyVersion { get; set; }
}
