using BarkFluff.Bots.Persistence;
using BarkFluff.Bots.Persistence.Services;

using Microsoft.EntityFrameworkCore;

namespace BarkFluff.Bots.Services;

/// <summary>
/// Раз в час: удаляет BotUpdates старше 24 часов (подтверждённые удаляются сразу в Confirm)
/// и BotFatherSessions старше 30 минут (TTL диалога).
/// </summary>
public class BotsCleanupService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);
    private static readonly TimeSpan UpdatesRetention = TimeSpan.FromHours(24);
    private static readonly TimeSpan SessionsTtl = TimeSpan.FromMinutes(30);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BotsCleanupService> _logger;

    public BotsCleanupService(IServiceScopeFactory scopeFactory, ILogger<BotsCleanupService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();

                var updatesStorage = scope.ServiceProvider.GetRequiredService<BotUpdatesStorage>();
                var deletedUpdates = await updatesStorage.DeleteOlderThan(DateTime.UtcNow - UpdatesRetention);

                var context = scope.ServiceProvider.GetRequiredService<BotsContext>();
                var deletedSessions = await context.BotFatherSessions
                    .Where(s => s.UpdatedAt < DateTime.UtcNow - SessionsTtl)
                    .ExecuteDeleteAsync(stoppingToken);

                if (deletedUpdates > 0 || deletedSessions > 0)
                {
                    _logger.LogInformation(
                        "Очистка: удалено {Updates} устаревших update'ов и {Sessions} сессий BotFather",
                        deletedUpdates, deletedSessions);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Ошибка фоновой очистки Bots");
            }
        }
    }
}
