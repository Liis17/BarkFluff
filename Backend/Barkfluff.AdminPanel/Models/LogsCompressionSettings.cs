namespace Barkfluff.AdminPanel.Models;

public class LogsCompressionSettings
{
    public const string SectionName = "LogsCompression";

    public bool Enabled { get; set; } = true;
    public int ScheduleUtcHour { get; set; } = 3;
    public int ScheduleUtcMinute { get; set; } = 0;
    public int MaxEventsPerRun { get; set; } = 500_000;
    public bool DryRun { get; set; } = false;
    public string SourceMessageTemplate { get; set; } = "ServiceMetrics {@Metrics}";
    public string SummaryMessagePrefix { get; set; } = "MetricsDailySummary";
}
