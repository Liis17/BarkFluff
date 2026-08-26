using System.Text.Json;

using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Messages.Domain;
using BarkFluff.Messages.Infrastructure;
using BarkFluff.Messages.Persistence;
using BarkFluff.Shared.Queue.Messages;

using Microsoft.EntityFrameworkCore;

namespace BarkFluff.Messages.BackgroundServices;

/// <summary>
/// Publishes durable NewMessageEvent rows. A crash after publish and before Delivered can
/// publish the same EventId again, so delivery is intentionally at-least-once.
/// </summary>
public class MessageOutboxDispatcher : BackgroundService
{
    private static readonly TimeSpan LoopInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ProcessingLease = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan RetryWindow = TimeSpan.FromDays(7);
    private static readonly TimeSpan[] BackoffSteps =
    [
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(30),
        TimeSpan.FromMinutes(2),
        TimeSpan.FromMinutes(10),
        TimeSpan.FromMinutes(30),
    ];
    private const int MaxBatchSize = 100;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly MetricsCollector _metrics;
    private readonly ILogger<MessageOutboxDispatcher> _logger;

    public MessageOutboxDispatcher(
        IServiceScopeFactory scopeFactory,
        MetricsCollector metrics,
        ILogger<MessageOutboxDispatcher> logger)
    {
        _scopeFactory = scopeFactory;
        _metrics = metrics;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(LoopInterval);
        do
        {
            try
            {
                await DispatchOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Ошибка диспетчера outbox Messages");
                _metrics.Increment("message_outbox_dispatch_errors");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    public async Task DispatchOnceAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MessagesContext>();
        var sender = scope.ServiceProvider.GetRequiredService<MessageQueueSender>();
        var now = DateTime.UtcNow;

        var expired = await context.MessageOutbox
            .Where(entry => entry.Status == MessageOutboxStatus.Processing && entry.NextAttemptAt <= now)
            .ToListAsync(cancellationToken);
        if (expired.Count > 0)
        {
            foreach (var entry in expired)
                entry.Status = MessageOutboxStatus.Pending;
            await context.SaveChangesAsync(cancellationToken);
            _metrics.Add("message_outbox_reclaimed", expired.Count);
        }

        var batch = await ClaimBatchAsync(context, now, cancellationToken);
        foreach (var entry in batch)
        {
            try
            {
                var queueEvent = JsonSerializer.Deserialize<NewMessageEvent>(entry.Payload)
                    ?? throw new JsonException("Пустой payload outbox");
                queueEvent.EventId = entry.EventId;
                await sender.PublishOutbox(queueEvent, cancellationToken);

                entry.Status = MessageOutboxStatus.Delivered;
                entry.LastError = null;
                _metrics.Increment("message_outbox_delivered");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                ScheduleRetry(entry, exception, now);
            }

            await context.SaveChangesAsync(cancellationToken);
        }

        var pending = await context.MessageOutbox
            .CountAsync(entry => entry.Status == MessageOutboxStatus.Pending, cancellationToken);
        _metrics.Set("message_outbox_pending", pending);
    }

    private static async Task<List<MessageOutboxEntry>> ClaimBatchAsync(
        MessagesContext context,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var isNpgsql = context.Database.IsNpgsql();
        await using var transaction = isNpgsql
            ? await context.Database.BeginTransactionAsync(cancellationToken)
            : null;

        List<MessageOutboxEntry> batch;
        if (isNpgsql)
        {
            batch = await context.MessageOutbox.FromSqlInterpolated($"""
                SELECT * FROM "MessageOutbox"
                WHERE "Status" = 0 AND "NextAttemptAt" <= {now}
                ORDER BY "Id"
                LIMIT {MaxBatchSize}
                FOR UPDATE SKIP LOCKED
                """).ToListAsync(cancellationToken);
        }
        else
        {
            batch = await context.MessageOutbox
                .Where(entry => entry.Status == MessageOutboxStatus.Pending && entry.NextAttemptAt <= now)
                .OrderBy(entry => entry.Id)
                .Take(MaxBatchSize)
                .ToListAsync(cancellationToken);
        }

        foreach (var entry in batch)
        {
            entry.Status = MessageOutboxStatus.Processing;
            entry.NextAttemptAt = now + ProcessingLease;
        }

        await context.SaveChangesAsync(cancellationToken);
        if (transaction is not null)
            await transaction.CommitAsync(cancellationToken);
        return batch;
    }

    private void ScheduleRetry(MessageOutboxEntry entry, Exception exception, DateTime now)
    {
        entry.Attempts += 1;
        entry.LastError = exception.Message.Length <= 2_000
            ? exception.Message
            : exception.Message[..2_000];

        if (now - entry.CreatedAt >= RetryWindow)
        {
            entry.Status = MessageOutboxStatus.DeadLetter;
            _metrics.Increment("message_outbox_deadletter");
        }
        else
        {
            entry.Status = MessageOutboxStatus.Pending;
            entry.NextAttemptAt = now + GetBackoff(entry.Attempts);
            _metrics.Increment("message_outbox_delivery_errors");
        }

        _logger.LogWarning(
            exception,
            "Не удалось опубликовать outbox event {EventId}, попытка {Attempt}",
            entry.EventId,
            entry.Attempts);
    }

    private static TimeSpan GetBackoff(int attempts)
    {
        var index = Math.Clamp(attempts - 1, 0, BackoffSteps.Length - 1);
        return BackoffSteps[index];
    }
}
