using BarkFluff.Federation.Domain.Enums;
using BarkFluff.Federation.Persistence.Contexts;
using BarkFluff.Federation.Services;

using Microsoft.EntityFrameworkCore;

namespace BarkFluff.Federation.BackgroundServices;

// Janitor: чистит delivered-записи outbox и ProcessedEvents по TTL (этап 2.2).
// Окна взяты с запасом: 7 дней для Delivered (для отладки) и 14 дней для ProcessedEvents
// (больше максимального окна ретраев отправителя). Конфигурируется.
//
// Single-runner (масштабирование, docs/scaling/federation.md): чистку выполняет один инстанс
// под Redis-локом; DELETE идемпотентен, поэтому best-effort-лок достаточен.
public class OutboxJanitor : BackgroundService
{
    private static readonly TimeSpan LoopInterval = TimeSpan.FromHours(1);
    private static readonly TimeSpan DefaultDeliveredTtl = TimeSpan.FromDays(7);
    private static readonly TimeSpan DefaultProcessedTtl = TimeSpan.FromDays(14);

    private const string LockKey = "federation:lock:outbox-janitor";

    // TTL с запасом против часового интервала: лидер продлевает каждый тик; при падении лидера
    // лок истекает и задачу подхватывает другой инстанс.
    private static readonly TimeSpan LockTtl = TimeSpan.FromHours(2);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ISingleRunner _singleRunner;
    private readonly ILogger<OutboxJanitor> _logger;

    public OutboxJanitor(IServiceScopeFactory scopeFactory, ISingleRunner singleRunner, ILogger<OutboxJanitor> logger)
    {
        _scopeFactory = scopeFactory;
        _singleRunner = singleRunner;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(LoopInterval);
        do
        {
            try
            {
                // Single-runner: чистку делает один инстанс, остальные пропускают тик.
                if (!await _singleRunner.TryAcquireAsync(LockKey, LockTtl))
                    continue;

                await CleanupOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка janitor'а outbox Federation");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task CleanupOnceAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<FederationContext>();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();

        var deliveredTtl = ParseTtl(configuration["Federation:OutboxDeliveredTtlHours"], DefaultDeliveredTtl);
        var processedTtl = ParseTtl(configuration["Federation:ProcessedEventsTtlHours"], DefaultProcessedTtl);

        var now = DateTime.UtcNow;

        var deliveredCutoff = now - deliveredTtl;
        var deletedDelivered = await context.Outbox
            .Where(r => r.Status == OutboxStatus.Delivered && r.CreatedAt < deliveredCutoff)
            .ExecuteDeleteAsync(ct);

        var processedCutoff = now - processedTtl;
        var deletedProcessed = await context.ProcessedEvents
            .Where(e => e.ReceivedAt < processedCutoff)
            .ExecuteDeleteAsync(ct);

        if (deletedDelivered > 0 || deletedProcessed > 0)
            _logger.LogInformation("Outbox janitor: удалено delivered={Delivered}, processed={Processed}", deletedDelivered, deletedProcessed);
    }

    private static TimeSpan ParseTtl(string? raw, TimeSpan fallback)
    {
        if (int.TryParse(raw, out var hours) && hours > 0)
            return TimeSpan.FromHours(hours);
        return fallback;
    }
}
