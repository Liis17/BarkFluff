using BarkFluff.Onliner.Persistence.Contexts;
using BarkFluff.Onliner.Services;

using Microsoft.EntityFrameworkCore;

namespace BarkFluff.Onliner.BackgroundServices;

/// <summary>
/// Background service для периодического сохранения статусов в БД
/// Выполняется каждые 10 минут
/// </summary>
public class DatabasePersistenceService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly OnlineStatusStorage _storage;
    private readonly ILogger<DatabasePersistenceService> _logger;

    private static readonly TimeSpan SaveInterval = TimeSpan.FromMinutes(10);

    public DatabasePersistenceService(
        IServiceProvider serviceProvider,
        OnlineStatusStorage storage,
        ILogger<DatabasePersistenceService> logger)
    {
        _serviceProvider = serviceProvider;
        _storage = storage;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Database Persistence Service started. Save interval: {Interval} minutes",
            SaveInterval.TotalMinutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(SaveInterval, stoppingToken);

            try
            {
                await SaveStatusesToDatabaseAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during database persistence cycle");
            }
        }

        _logger.LogInformation("Database Persistence Service stopped");
    }

    private async Task SaveStatusesToDatabaseAsync(CancellationToken cancellationToken)
    {
        var allStatuses = _storage.GetAllStatuses();

        if (allStatuses.Count == 0)
        {
            _logger.LogDebug("No statuses to save to database");
            return;
        }

        _logger.LogInformation("Saving {Count} statuses to database", allStatuses.Count);

        // Создаем scope для scoped DbContext
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OnlineStatusContext>();

        try
        {
            // Используем bulk update/insert подход
            foreach (var status in allStatuses)
            {
                var existing = await dbContext.UsersOnlineStatuses
                    .FirstOrDefaultAsync(s => s.UserId == status.UserId, cancellationToken);

                if (existing != null)
                {
                    // Update
                    existing.Status = status.Status;
                    existing.LastSeen = status.LastSeen;
                }
                else
                {
                    // Insert
                    dbContext.UsersOnlineStatuses.Add(status);
                }
            }

            var savedCount = await dbContext.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Successfully saved {Count} status records to database", savedCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save statuses to database");
            throw;
        }
    }
}
