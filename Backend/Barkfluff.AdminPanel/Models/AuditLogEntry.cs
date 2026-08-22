using LiteDB;

namespace Barkfluff.AdminPanel.Models;

public class AuditLogEntry
{
    [BsonId]
    public ObjectId Id { get; set; } = ObjectId.NewObjectId();

    [BsonField("at")]
    public DateTime At { get; set; } = DateTime.UtcNow;

    [BsonField("admin_username")]
    public string? AdminUsername { get; set; }

    [BsonField("telegram_user_id")]
    public long? TelegramUserId { get; set; }

    [BsonField("action")]
    public string Action { get; set; } = string.Empty;

    [BsonField("details")]
    public string Details { get; set; } = string.Empty;

    [BsonField("ip")]
    public string? IpAddress { get; set; }

    [BsonField("confirmation_id")]
    public string? ConfirmationId { get; set; }

    [BsonField("outcome")]
    public string Outcome { get; set; } = "ok";
}
