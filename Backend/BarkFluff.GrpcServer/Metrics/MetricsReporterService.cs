using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BarkFluff.GrpcServer.Metrics;

/// <summary>
/// Фоновый сервис, который каждые 5 секунд публикует структурированные метрики в логи.
/// </summary>
public class MetricsReporterService : BackgroundService
{
    // 60 тиков по 5 секунд = 5 минут: максимальная частота heartbeat-а в простое.
    private const int IdleHeartbeatEveryTicks = 60;

    private readonly MetricsCollector _collector;
    private readonly ILogger<MetricsReporterService> _logger;
    private readonly string _serviceName;
    private int _idleTicks;

    public MetricsReporterService(
        MetricsCollector collector,
        ILogger<MetricsReporterService> logger,
        string serviceName)
    {
        _collector = collector;
        _logger = logger;
        _serviceName = serviceName;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(5000, stoppingToken);

            var snapshot = _collector.SnapshotAndResetDetailed(out var hadCounterActivity);

            bool shouldReport;
            if (hadCounterActivity)
            {
                // Есть реальная активность — публикуем полный снимок (counters + gauges).
                _idleTicks = 0;
                shouldReport = true;
            }
            else
            {
                // Простой: только статичные gauges. Шлём heartbeat не чаще раза в 5 минут,
                // чтобы не спамить Seq, но сохранить uptime/db_healthy в AdminPanel.
                _idleTicks++;
                shouldReport = snapshot.Gauges.Count > 0 && _idleTicks >= IdleHeartbeatEveryTicks;
                if (shouldReport)
                    _idleTicks = 0;
            }

            if (shouldReport)
            {
                _logger.LogInformation("ServiceMetrics {@Metrics}", new
                {
                    SchemaVersion = 2,
                    ServiceName = _serviceName,
                    Counters = snapshot.Counters,
                    Gauges = snapshot.Gauges,
                    Timestamp = DateTime.UtcNow
                });
            }
        }
    }
}
