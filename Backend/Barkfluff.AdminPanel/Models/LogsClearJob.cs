namespace Barkfluff.AdminPanel.Models;

public enum LogsClearState { Queued, Counting, Deleting, Done, Error }

public enum LogsClearScope { All, Old }

public class LogsClearJob
{
    public Guid Id { get; init; }
    public LogsClearScope Scope { get; init; }
    public LogsClearState State { get; set; }
    public long TotalCount { get; set; }
    public long DeletedCount { get; set; }
    public string? Error { get; set; }
    public DateTime CreatedAtUtc { get; init; }
    public DateTime UpdatedAtUtc { get; set; }
}
