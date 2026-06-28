using Barkfluff.AdminPanel.Models;

using LiteDB;

namespace Barkfluff.AdminPanel.Data;

public class MetricsCacheDbContext : IDisposable
{
    private readonly LiteDatabase _db;

    public MetricsCacheDbContext(MetricsCacheSettings settings)
    {
        var dbPath = settings.Path;
        if (!Path.IsPathRooted(dbPath))
        {
            dbPath = Path.Combine(AppContext.BaseDirectory, dbPath);
        }

        var directory = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _db = new LiteDatabase(dbPath);

        HourlyStats = _db.GetCollection<HourlyStats>("hourly_stats");
        HourlyStats.EnsureIndex(x => x.HourUtc, true);

        HourlyTraffic = _db.GetCollection<HourlyTraffic>("hourly_traffic");
        HourlyTraffic.EnsureIndex(x => x.HourUtc, true);

        HourlyServiceMetrics = _db.GetCollection<HourlyServiceMetrics>("hourly_service_metrics");
        HourlyServiceMetrics.EnsureIndex(x => x.HourUtc);
        HourlyServiceMetrics.EnsureIndex(x => x.ServiceName);

        CompressionRuns = _db.GetCollection<MetricsCompressionRun>("compression_runs");
    }

    public ILiteCollection<HourlyStats> HourlyStats { get; }
    public ILiteCollection<HourlyTraffic> HourlyTraffic { get; }
    public ILiteCollection<HourlyServiceMetrics> HourlyServiceMetrics { get; }
    public ILiteCollection<MetricsCompressionRun> CompressionRuns { get; }

    public void Dispose()
    {
        _db.Dispose();
    }
}

public class MetricsCacheSettings
{
    public const string SectionName = "MetricsCache";
    public string Path { get; set; } = "db/metrics_cache.db";
}

public class HourlyStats
{
    [BsonId]
    public DateTime HourUtc { get; set; }
    public long TotalEvents { get; set; }
    public long ErrorCount { get; set; }
    public long WarningCount { get; set; }
    public Dictionary<string, long> PerService { get; set; } = new();
}

public class HourlyTraffic
{
    [BsonId]
    public DateTime HourUtc { get; set; }
    public long AllCount { get; set; }
    public long ErrorCount { get; set; }
    public long WarningCount { get; set; }
}

public class HourlyServiceMetrics
{
    public ObjectId Id { get; set; } = ObjectId.NewObjectId();
    public DateTime HourUtc { get; set; }
    public string ServiceName { get; set; } = string.Empty;
    public Dictionary<string, long> Metrics { get; set; } = new();
}
