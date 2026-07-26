using LiteDB;

namespace Barkfluff.AdminPanel.Models;

public class AuthToken
{
    [BsonId]
    public Guid Id { get; set; }

    [BsonField("name")]
    public string Name { get; set; } = string.Empty;

    [BsonField("created_at")]
    public DateTime CreatedAt { get; set; }

    [BsonField("last_activity")]
    public DateTime LastActivity { get; set; }

    [BsonField("ip_address")]
    public string? IpAddress { get; set; }

    [BsonField("user_agent")]
    public string? UserAgent { get; set; }

    [BsonField("admin_username")]
    public string? AdminUsername { get; set; }

    [BsonField("approved_by")]
    public long? ApprovedByTelegramUserId { get; set; }

    [BsonField("last_security_alert_fingerprint")]
    public string? LastSecurityAlertFingerprint { get; set; }

    [BsonField("last_security_alert_at")]
    public DateTime? LastSecurityAlertAt { get; set; }

    public AuthToken()
    {
        Id = Guid.NewGuid();
        CreatedAt = DateTime.UtcNow;
        LastActivity = DateTime.UtcNow;
    }

    public bool IsExpired(int expirationDays)
    {
        return (DateTime.UtcNow - LastActivity).TotalDays > expirationDays;
    }

    /// <summary>
    /// Checks if this token is visible to the specified admin.
    /// Tokens without AdminUsername are not visible (will expire naturally).
    /// </summary>
    public bool IsVisibleToAdmin(long adminTelegramUserId, string adminUsername)
    {
        if (string.IsNullOrEmpty(AdminUsername) || !ApprovedByTelegramUserId.HasValue)
            return false;

        return ApprovedByTelegramUserId.Value == adminTelegramUserId;
    }
}
