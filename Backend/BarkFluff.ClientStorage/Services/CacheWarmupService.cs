using BarkFluff.ClientStorage.Domain;
using BarkFluff.ClientStorage.Infrastructure;
using BarkFluff.ClientStorage.Persistence;

using Microsoft.EntityFrameworkCore;

namespace BarkFluff.ClientStorage.Services;

public class CacheWarmupService : IHostedService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<CacheWarmupService> _logger;

    public CacheWarmupService(IServiceProvider services, ILogger<CacheWarmupService> logger)
    {
        _services = services;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _ = Task.Run(() => WarmUpAsync(cancellationToken), cancellationToken);
        return Task.CompletedTask;
    }

    private async Task WarmUpAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _services.CreateScope();
            var db    = scope.ServiceProvider.GetRequiredService<ClientStorageContext>();
            var s3    = scope.ServiceProvider.GetRequiredService<S3StorageService>();
            var cache = scope.ServiceProvider.GetRequiredService<LocalFileCache>();

            foreach (ClientType clientType in Enum.GetValues<ClientType>())
            {
                foreach (ReleaseChannel channel in Enum.GetValues<ReleaseChannel>())
                {
                    if (ct.IsCancellationRequested) return;

                    var file = await db.ClientFiles
                        .Where(f => f.ClientType == clientType && f.ReleaseChannel == channel)
                        .OrderByDescending(f => f.UploadedAt)
                        .FirstOrDefaultAsync(ct);

                    if (file == null) continue;

                    if (cache.GetCachedFilePath(clientType, channel) != null)
                    {
                        _logger.LogInformation("Кеш актуален: {ClientType} {Channel}", clientType, channel);
                        continue;
                    }

                    _logger.LogInformation("Прогрев кеша: {ClientType} {Channel} ({S3Key})", clientType, channel, file.S3Key);
                    try
                    {
                        using var result = await s3.DownloadAsync(file.S3Key);
                        await cache.UpdateAsync(clientType, channel, result.Stream);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Ошибка прогрева кеша: {ClientType} {Channel}", clientType, channel);
                    }
                }
            }

            _logger.LogInformation("Прогрев кеша завершён");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Критическая ошибка прогрева кеша");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
