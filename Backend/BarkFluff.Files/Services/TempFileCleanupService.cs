using BarkFluff.Files.Persistence;

namespace BarkFluff.Files.Services;

/// <summary>
/// Фоновая периодическая очистка просроченных записей TempFile из БД.
/// Без этого таблица бесконечно растёт, а запросы по индексу деградируют.
/// </summary>
public class TempFileCleanupService : BackgroundService
{
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromHours(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TempFileCleanupService> _logger;

    public TempFileCleanupService(IServiceScopeFactory scopeFactory, ILogger<TempFileCleanupService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var storage = scope.ServiceProvider.GetRequiredService<TempFilesStorage>();

                var deleted = await storage.DeleteExpiredAsync(stoppingToken);

                if (deleted > 0)
                    _logger.LogInformation("Удалено {Count} устаревших временных ссылок", deleted);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при очистке просроченных TempFile");
            }

            try
            {
                await Task.Delay(CleanupInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
