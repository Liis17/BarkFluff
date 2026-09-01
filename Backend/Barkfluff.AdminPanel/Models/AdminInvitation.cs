using LiteDB;

namespace Barkfluff.AdminPanel.Models;

public enum AdminInvitationStatus
{
    Pending = 0,
    Accepted = 1,
    Rejected = 2,
    Expired = 3
}

public class AdminInvitation
{
    [BsonId]
    public Guid Id { get; set; } = Guid.NewGuid();

    [BsonField("payload")]
    public string Payload { get; set; } = Guid.NewGuid().ToString("N");

    [BsonField("telegram_user_id")]
    public long TelegramUserId { get; set; }

    [BsonField("username")]
    public string Username { get; set; } = string.Empty;

    [BsonField("created_by")]
    public string CreatedBy { get; set; } = string.Empty;

    [BsonField("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [BsonField("expires_at")]
    public DateTime ExpiresAt { get; set; }

    [BsonField("status")]
    public AdminInvitationStatus Status { get; set; } = AdminInvitationStatus.Pending;

    [BsonField("resolved_at")]
    public DateTime? ResolvedAt { get; set; }

    [BsonField("resolved_by")]
    public long? ResolvedByTelegramUserId { get; set; }
}
