using BarkFluff.ClientStorage.Domain;
using BarkFluff.ClientStorage.Infrastructure;
using BarkFluff.ClientStorage.Persistence;

using Microsoft.EntityFrameworkCore;

namespace BarkFluff.ClientStorage.Services;

public class OldVersionsCleanupService : BackgroundService
{
    private static readonly TimeSpan StartupRetention = TimeSpan.FromDays(7);
    private static readonly TimeSpan VersionLimitCheckInterval = TimeSpan.FromHours(3);
    private static readonly TimeSpan DailyCleanupTime = TimeSpan.FromHours(3);
    private static readonly TimeZoneInfo MoscowTimeZone = CreateMoscowTimeZone();

    private readonly IServiceProvider _services;
    private readonly ILogger<OldVersionsCleanupService> _logger;
    private readonly SemaphoreSlim _cleanupGate = new(1, 1);

    public OldVersionsCleanupService(IServiceProvider services, ILogger<OldVersionsCleanupService> logger)
    {
        _services = services;
        _logger = logger;
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        await RunStartupCleanupAsync(cancellationToken);
        await base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var versionLimitTask = RunVersionLimitLoopAsync(stoppingToken);
        var dailyCleanupTask = RunDailyCleanupLoopAsync(stoppingToken);

        await Task.WhenAll(versionLimitTask, dailyCleanupTask);
    }

    private async Task RunStartupCleanupAsync(CancellationToken ct)
    {
        await _cleanupGate.WaitAsync(ct);

        try
        {
            _logger.LogInformation(
                "Запуск стартовой очистки файлов старше {RetentionDays} дней",
                StartupRetention.TotalDays);

            using var scope = _services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ClientStorageContext>();
            var s3 = scope.ServiceProvider.GetRequiredService<S3StorageService>();
            var cutoff = DateTime.UtcNow - StartupRetention;

            var files = await SelectStartupFilesForDeletion(db.ClientFiles, cutoff)
                .ToListAsync(ct);

            var totalDeleted = await DeleteFilesAsync(db, s3, files, ct);
            await SaveChangesIfNeededAsync(db, totalDeleted, ct);

            _logger.LogInformation(
                "Стартовая очистка завершена. Удалено записей: {Count}", totalDeleted);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Критическая ошибка при стартовой очистке старых версий");
        }
        finally
        {
            _cleanupGate.Release();
        }
    }

    private async Task RunVersionLimitLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(VersionLimitCheckInterval);

        while (await timer.WaitForNextTickAsync(ct))
        {
            await RunKeepLatestCleanupAsync(keepCount: 3, "трёхчасовой", ct);
        }
    }

    private async Task RunDailyCleanupLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var delay = GetDelayUntilNextDailyCleanup(DateTime.UtcNow);
            await Task.Delay(delay, ct);
            await RunKeepLatestCleanupAsync(keepCount: 1, "ежедневной", ct);
        }
    }

    private async Task RunKeepLatestCleanupAsync(int keepCount, string cleanupName, CancellationToken ct)
    {
        await _cleanupGate.WaitAsync(ct);

        try
        {
            _logger.LogInformation(
                "Запуск {CleanupName} очистки старых версий. Оставляем: {KeepCount}",
                cleanupName, keepCount);

            int totalDeleted = 0;
            using var scope = _services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ClientStorageContext>();
            var s3 = scope.ServiceProvider.GetRequiredService<S3StorageService>();

            foreach (ClientType clientType in Enum.GetValues<ClientType>())
            {
                foreach (ReleaseChannel channel in Enum.GetValues<ReleaseChannel>())
                {
                    if (ct.IsCancellationRequested) return;

                    var files = await db.ClientFiles
                        .Where(f => f.ClientType == clientType && f.ReleaseChannel == channel)
                        .OrderByDescending(f => f.UploadedAt)
                        .ThenByDescending(f => f.Id)
                        .ToListAsync(ct);

                    if (files.Count <= keepCount) continue;

                    var toDelete = files.Skip(keepCount).ToList();
                    var deletedForGroup = await DeleteFilesAsync(db, s3, toDelete, ct);
                    totalDeleted += deletedForGroup;

                    await SaveChangesIfNeededAsync(db, deletedForGroup, ct);
                }
            }

            _logger.LogInformation(
                "{CleanupName} очистка завершена. Удалено записей: {Count}",
                cleanupName, totalDeleted);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Критическая ошибка при {CleanupName} очистке старых версий", cleanupName);
        }
        finally
        {
            _cleanupGate.Release();
        }
    }

    private async Task<int> DeleteFilesAsync(
        ClientStorageContext db,
        S3StorageService s3,
        IEnumerable<ClientFile> files,
        CancellationToken ct)
    {
        var totalDeleted = 0;

        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                await s3.DeleteAsync(file.S3Key, ct);
                db.ClientFiles.Remove(file);
                totalDeleted++;
                _logger.LogInformation(
                    "Удалена старая версия: {ClientType} {Channel} v{Version} ({S3Key})",
                    file.ClientType, file.ReleaseChannel, file.Version ?? "—", file.S3Key);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Ошибка удаления {ClientType} {Channel} {S3Key}",
                    file.ClientType, file.ReleaseChannel, file.S3Key);
            }
        }

        return totalDeleted;
    }

    private static Task SaveChangesIfNeededAsync(
        ClientStorageContext db,
        int deletedCount,
        CancellationToken ct)
        => deletedCount > 0 ? db.SaveChangesAsync(ct) : Task.CompletedTask;

    internal static IQueryable<ClientFile> SelectStartupFilesForDeletion(
        IQueryable<ClientFile> files,
        DateTime cutoff)
        => files
            .Where(f => f.UploadedAt < cutoff)
            .Where(f => files.Any(newer =>
                newer.ClientType == f.ClientType &&
                newer.ReleaseChannel == f.ReleaseChannel &&
                (newer.UploadedAt > f.UploadedAt ||
                 newer.UploadedAt == f.UploadedAt && newer.Id > f.Id)))
            .OrderBy(f => f.UploadedAt);

    private static TimeSpan GetDelayUntilNextDailyCleanup(DateTime utcNow)
    {
        var utc = DateTime.SpecifyKind(utcNow, DateTimeKind.Utc);
        var moscowNow = TimeZoneInfo.ConvertTimeFromUtc(utc, MoscowTimeZone);
        var nextDate = moscowNow.TimeOfDay < DailyCleanupTime
            ? moscowNow.Date
            : moscowNow.Date.AddDays(1);
        var nextMoscow = nextDate.Add(DailyCleanupTime);
        var nextUtc = TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(nextMoscow, DateTimeKind.Unspecified), MoscowTimeZone);

        return nextUtc - utc > TimeSpan.Zero
            ? nextUtc - utc
            : TimeSpan.FromSeconds(1);
    }

    private static TimeZoneInfo CreateMoscowTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Europe/Moscow");
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return TimeZoneInfo.CreateCustomTimeZone(
                "Europe/Moscow", TimeSpan.FromHours(3), "Europe/Moscow", "Europe/Moscow");
        }
    }
}
