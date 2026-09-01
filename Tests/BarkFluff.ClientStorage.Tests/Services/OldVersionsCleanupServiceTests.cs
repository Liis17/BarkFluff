using BarkFluff.ClientStorage.Domain;
using BarkFluff.ClientStorage.Persistence;
using BarkFluff.ClientStorage.Services;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Xunit;

namespace BarkFluff.ClientStorage.Tests.Services;

public class OldVersionsCleanupServiceTests
{
    [Fact]
    public async Task StartupCleanup_preserves_latest_file_in_stale_branch_and_deletes_older_versions()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ClientStorageContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new ClientStorageContext(options);
        await db.Database.EnsureCreatedAsync();

        var cutoff = new DateTime(2026, 8, 31, 0, 0, 0, DateTimeKind.Utc);
        var olderVersion = CreateFile(1, ClientType.Windows, ReleaseChannel.Release, cutoff.AddDays(-30));
        var latestVersion = CreateFile(2, ClientType.Windows, ReleaseChannel.Release, cutoff.AddDays(-20));
        var onlyVersion = CreateFile(3, ClientType.Windows, ReleaseChannel.Beta, cutoff.AddDays(-30));
        var recentVersion = CreateFile(4, ClientType.Windows, ReleaseChannel.Dev, cutoff.AddDays(1));

        db.ClientFiles.AddRange(olderVersion, latestVersion, onlyVersion, recentVersion);
        await db.SaveChangesAsync();

        var filesToDelete = await OldVersionsCleanupService
            .SelectStartupFilesForDeletion(db.ClientFiles, cutoff)
            .ToListAsync();

        Assert.Equal([olderVersion.Id], filesToDelete.Select(file => file.Id));
    }

    private static ClientFile CreateFile(
        int id,
        ClientType clientType,
        ReleaseChannel channel,
        DateTime uploadedAt)
        => new()
        {
            Id = id,
            ClientType = clientType,
            ReleaseChannel = channel,
            OriginalFileName = $"client-{id}.exe",
            S3Key = $"client-{id}",
            ContentType = "application/octet-stream",
            FileSize = 1,
            Checksum = $"checksum-{id}",
            UploadedAt = uploadedAt,
            Version = id.ToString()
        };
}
