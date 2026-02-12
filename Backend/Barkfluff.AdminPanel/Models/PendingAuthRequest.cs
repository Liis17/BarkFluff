namespace Barkfluff.Docker.Control.Models;

public class PendingAuthRequest
{
    public string RequestId { get; set; } = Guid.NewGuid().ToString();
    public string? IpAddress { get; set; }
    public string? Browser { get; set; }
    public string? Os { get; set; }
    public string? UserAgent { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public AuthRequestStatus Status { get; set; } = AuthRequestStatus.Pending;
    public int? TelegramMessageId { get; set; }
    public Guid? TokenId { get; set; }
    public string? TokenName { get; set; }
    public long? ApprovedByTelegramUserId { get; set; }
    public DateTime? CompletedAt { get; set; }
}

public enum AuthRequestStatus
{
    Pending,
    Approved,
    Rejected,
    Expired
}
