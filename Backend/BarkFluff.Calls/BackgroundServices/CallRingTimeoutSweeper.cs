using BarkFluff.Calls.Domain;
using BarkFluff.Calls.Persistence;
using BarkFluff.Calls.Services;

using Microsoft.EntityFrameworkCore;

namespace BarkFluff.Calls.BackgroundServices;

/// <summary>
/// Durable-детектор таймаута ринга: периодически находит звонки в статусе Ringing старше
/// <see cref="RingTimeout"/> и помечает их пропущенными через <see cref="CallsService.TimeoutAsync"/>.
/// Атомарный захват в TimeoutAsync делает обработку ровно-однократной при нескольких инстансах,
/// а опрос БД (вместо in-memory таймера) переживает перезапуск инстанса и не требует плагина
/// RabbitMQ delayed-exchange (см. docs/scaling/calls.md).
/// </summary>
public class CallRingTimeoutSweeper(IServiceScopeFactory scopeFactory, ILogger<CallRingTimeoutSweeper> logger)
    : BackgroundService
{
    public static readonly TimeSpan RingTimeout = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(SweepInterval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await SweepAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Ошибка прохода таймаута звонков");
            }
        }
    }

    private async Task SweepAsync(CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow - RingTimeout;

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CallsContext>();
        var calls = scope.ServiceProvider.GetRequiredService<CallsService>();

        var expired = await db.CallSessions.AsNoTracking()
            .Where(c => c.Status == CallStatus.Ringing && c.StartedAt < cutoff)
            .Select(c => c.Id)
            .ToListAsync(ct);

        foreach (var id in expired)
        {
            // TimeoutAsync атомарно захватывает звонок — параллельные инстансы не задублируют обработку.
            await calls.TimeoutAsync(id, ct);
        }
    }
}
