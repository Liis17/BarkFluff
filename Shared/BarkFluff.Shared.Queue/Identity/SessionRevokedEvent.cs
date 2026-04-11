namespace BarkFluff.Shared.Queue.Identity;

public class SessionRevokedEvent
{
    public long UserId { get; set; }

    public string DeviceId { get; set; } = string.Empty;

    public DateTime AccessTokenExpiresAt { get; set; }
}
