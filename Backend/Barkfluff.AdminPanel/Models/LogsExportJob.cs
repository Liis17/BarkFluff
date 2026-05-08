namespace Barkfluff.AdminPanel.Models;

public enum LogsExportState
{
    Queued,
    Downloading,
    Compressing,
    Ready,
    Error
}

public enum LogsExportScope
{
    All,
    Old
}

public class LogsExportJob
{
    public Guid Id { get; init; }
    public LogsExportScope Scope { get; init; }
    public LogsExportState State { get; set; }
    public int TotalDownloaded { get; set; }
    public int CurrentPage { get; set; }
    public string TempDir { get; init; } = string.Empty;
    public string? ZipPath { get; set; }
    public long? ZipSizeBytes { get; set; }
    public string? Error { get; set; }
    public DateTime CreatedAtUtc { get; init; }
    public DateTime UpdatedAtUtc { get; set; }
}
