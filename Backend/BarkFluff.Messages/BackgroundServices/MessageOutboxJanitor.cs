using BarkFluff.Messages.Domain;
using BarkFluff.Messages.Persistence;

using Microsoft.EntityFrameworkCore;

namespace BarkFluff.Messages.BackgroundServices;

public class MessageOutboxJanitor : BackgroundService
{
    private static readonly TimeSpan LoopInterval = TimeSpan.FromHours(1);
    private static readonly TimeSpan DeliveredTtl = TimeSpan.FromDays(7);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<MessageOutboxJanitor> _logger;

    public MessageOutboxJanitor(
        IServiceScopeFactory scopeFactory,
        ILogger<MessageOutboxJanitor> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(LoopInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await CleanupOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Ошибка janitor outbox Messages");
            }
        }
    }

    public async Task<int> CleanupOnceAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MessagesContext>();
        var cutoff = DateTime.UtcNow - DeliveredTtl;

        int deleted;
        if (context.Database.IsRelational())
        {
            deleted = await context.MessageOutbox
                .Where(entry => entry.Status == MessageOutboxStatus.Delivered && entry.CreatedAt < cutoff)
                .ExecuteDeleteAsync(cancellationToken);
        }
        else
        {
            var expired = await context.MessageOutbox
                .Where(entry => entry.Status == MessageOutboxStatus.Delivered && entry.CreatedAt < cutoff)
                .ToListAsync(cancellationToken);
            context.MessageOutbox.RemoveRange(expired);
            await context.SaveChangesAsync(cancellationToken);
            deleted = expired.Count;
        }

        if (deleted > 0)
            _logger.LogInformation("Outbox Messages: удалено delivered={Delivered}", deleted);
        return deleted;
    }
}
