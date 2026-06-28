using BarkFluff.GrpcServer.Metrics;

namespace BarkFluff.FastAuth.Infrastructure;

/// <summary>
/// Тикает раз в 30 секунд: помечает истёкшие сессии EXPIRED (закрывая их стрим)
/// и удаляет финализированные сессии старше FinalRetention.
/// </summary>
public class FastAuthExpirationService(
    FastAuthSessionsManager manager,
    MetricsCollector metrics,
    ILogger<FastAuthExpirationService> logger) : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("FastAuth expiration service started, tick interval {Interval}s",
            TickInterval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                Sweep();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in FastAuth expiration sweep");
            }

            await Task.Delay(TickInterval, stoppingToken);
        }
    }

    private void Sweep()
    {
        var now = DateTime.UtcNow;

        foreach (var session in manager.Snapshot())
        {
            if (!session.IsFinal && now >= session.ExpiresAt)
            {
                if (session.TryExpire())
                {
                    metrics.Increment("sessions_expired");
                    logger.LogInformation("FastAuth session {Id} expired", session.Id[..8]);
                }
            }

            if (session.IsFinal && session.FinalizedAt.HasValue
                && now - session.FinalizedAt.Value > FastAuthSessionsManager.FinalRetention)
            {
                if (manager.Remove(session.Id))
                {
                    metrics.Increment("sessions_removed");
                }
            }
        }
    }
}
