using LiteDB;

namespace Barkfluff.AdminPanel.Models;

public class MetricsCompressionRun
{
    [BsonId]
    public DateTime DayUtc { get; set; }
    public DateTime CompletedAtUtc { get; set; }
    public int ServiceCount { get; set; }
    public int SourceEventCount { get; set; }
    public int DeletedCount { get; set; }
    public bool DryRun { get; set; }
}
