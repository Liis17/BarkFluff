using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BarkFluff.GrpcServer.Metrics;

/// <summary>
/// Фоновый сервис, который каждые 5 секунд публикует структурированные метрики в логи.
/// </summary>
public class MetricsReporterService : BackgroundService
{
    private readonly MetricsCollector _collector;
    private readonly ILogger<MetricsReporterService> _logger;
    private readonly string _serviceName;

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

            var snapshot = _collector.SnapshotAndReset();

            if (snapshot.Count > 0)
            {
                _logger.LogInformation("ServiceMetrics {@Metrics}",
                    new { ServiceName = _serviceName, Metrics = snapshot, Timestamp = DateTime.UtcNow });
            }
        }
    }
}
