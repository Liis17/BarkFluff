using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BarkFluff.GrpcServer.Metrics;

/// <summary>
/// Экспортирует мгновенные бизнес-события сразу, а high-throughput counters — раз в 10 секунд.
/// </summary>
public class MetricsReporterService : BackgroundService
{
    private static readonly TimeSpan BufferedFlushInterval = TimeSpan.FromSeconds(10);

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
        var immediateReader = _collector.ImmediateSnapshots;

        while (!stoppingToken.IsCancellationRequested)
        {
            using var waitCancellation = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            var immediateAvailable = immediateReader.WaitToReadAsync(waitCancellation.Token).AsTask();
            var nextFlush = Task.Delay(BufferedFlushInterval, waitCancellation.Token);
            var completed = await Task.WhenAny(immediateAvailable, nextFlush);
            waitCancellation.Cancel();

            if (completed == immediateAvailable && await immediateAvailable)
            {
                while (immediateReader.TryRead(out var snapshot))
                    Report(snapshot);
            }

            if (completed == nextFlush && !stoppingToken.IsCancellationRequested)
            {
                var snapshot = _collector.TakeBufferedSnapshot();
                if (snapshot.Counters.Count != 0 || snapshot.Gauges.Count != 0)
                    Report(snapshot);
            }
        }
    }

    private void Report(MetricsSnapshot snapshot)
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
