using BarkFluff.ClientStorage.Domain;
using BarkFluff.ClientStorage.Infrastructure;
using BarkFluff.ClientStorage.Persistence;

using Microsoft.EntityFrameworkCore;

namespace BarkFluff.ClientStorage.Services;

public class OldVersionsCleanupService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<OldVersionsCleanupService> _logger;
    private readonly TimeSpan _interval = TimeSpan.FromDays(7);

    public OldVersionsCleanupService(IServiceProvider services, ILogger<OldVersionsCleanupService> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(_interval, stoppingToken);
            await RunCleanupAsync(stoppingToken);
        }
    }

    private async Task RunCleanupAsync(CancellationToken ct)
    {
        _logger.LogInformation("Запуск еженедельной очистки старых версий дистрибутивов");

        try
        {
            using var scope = _services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ClientStorageContext>();
            var s3 = scope.ServiceProvider.GetRequiredService<S3StorageService>();

            int totalDeleted = 0;

            foreach (ClientType clientType in Enum.GetValues<ClientType>())
            {
                foreach (ReleaseChannel channel in Enum.GetValues<ReleaseChannel>())
                {
                    if (ct.IsCancellationRequested) return;

                    var files = await db.ClientFiles
                        .Where(f => f.ClientType == clientType && f.ReleaseChannel == channel)
                        .OrderByDescending(f => f.UploadedAt)
                        .ToListAsync(ct);

                    if (files.Count <= 1) continue;

                    var toDelete = files.Skip(1).ToList(); // все кроме последней

                    foreach (var file in toDelete)
                    {
                        try
                        {
                            await s3.DeleteAsync(file.S3Key);
                            db.ClientFiles.Remove(file);
                            totalDeleted++;
                            _logger.LogInformation(
                                "Удалена старая версия: {ClientType} {Channel} v{Version} ({S3Key})",
                                clientType, channel, file.Version ?? "—", file.S3Key);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex,
                                "Ошибка удаления {ClientType} {Channel} {S3Key}",
                                clientType, channel, file.S3Key);
                        }
                    }

                    await db.SaveChangesAsync(ct);
                }
            }

            _logger.LogInformation("Очистка завершена. Удалено записей: {Count}", totalDeleted);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Критическая ошибка при очистке старых версий");
        }
    }
}
