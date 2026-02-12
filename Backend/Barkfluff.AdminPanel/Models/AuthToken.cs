using LiteDB;

namespace Barkfluff.Docker.Control.Models;

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
}
