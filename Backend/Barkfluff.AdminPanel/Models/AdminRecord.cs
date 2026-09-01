using LiteDB;

namespace Barkfluff.AdminPanel.Models;

public class AdminRecord
{
    [BsonId]
    public long TelegramUserId { get; set; }

    [BsonField("username")]
    public string Username { get; set; } = string.Empty;

    [BsonField("roles")]
    public List<string> Roles { get; set; } = new();

    [BsonField("created_at")]
    public DateTime CreatedAt { get; set; }

    [BsonField("updated_at")]
    public DateTime UpdatedAt { get; set; }

    [BsonField("updated_by")]
    public string? UpdatedBy { get; set; }

    public AdminRecord()
    {
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    [BsonIgnore]
    public HashSet<AdminRole> RoleSet => AdminRoles.ParseNames(Roles);

    [BsonIgnore]
    public bool IsOwner => RoleSet.Contains(AdminRole.Owner);
}
