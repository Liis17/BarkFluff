using BarkFluff.Files.Persistence;

namespace BarkFluff.Files.Services;

/// <summary>
/// Фоновая периодическая очистка просроченных записей TempFile и незагруженных upload-слотов из БД.
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
                var tempFilesStorage = scope.ServiceProvider.GetRequiredService<TempFilesStorage>();
                var uploadedFilesStorage = scope.ServiceProvider.GetRequiredService<UploadedFilesStorage>();

                var deletedTempFiles = await tempFilesStorage.DeleteExpiredAsync(stoppingToken);
                if (deletedTempFiles > 0)
                    _logger.LogInformation("Удалено {Count} устаревших временных ссылок", deletedTempFiles);

                var deletedPendingUploads = await uploadedFilesStorage.DeleteExpiredPendingAsync(stoppingToken);
                if (deletedPendingUploads > 0)
                    _logger.LogInformation("Удалено {Count} незагруженных upload-слотов с истёкшим TTL", deletedPendingUploads);
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
