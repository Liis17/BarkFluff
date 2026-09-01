using BarkFluff.Files.Domain;
using BarkFluff.Files.Persistence;

namespace BarkFluff.Files.Tests.Persistence;

public class UploadedFilesStorageTests : IAsyncLifetime
{
    private readonly TestHelper _helper = new();
    private UploadedFilesStorage Storage => _helper.UploadedFilesStorage;

    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task AddToStorage_SavesFile()
    {
        var file = new UploadFile
        {
            Id = Guid.NewGuid(),
            Uploaders = [1],
            Type = UploadFileType.MessageAttachmentImage,
            CreatedAt = DateTime.UtcNow,
            Filename = "test.jpg"
        };

        var result = await Storage.AddToStorage(file);

        result.Should().NotBeNull();
        result.Id.Should().Be(file.Id);

        var fetched = await Storage.GetFile(file.Id);
        fetched.Should().NotBeNull();
        fetched!.Filename.Should().Be("test.jpg");
    }

    [Fact]
    public async Task GetFile_NotFound_ReturnsNull()
    {
        var result = await Storage.GetFile(Guid.NewGuid());
        result.Should().BeNull();
    }

    [Fact]
    public async Task UpdateFile_UpdatesFields()
    {
        var file = await _helper.SeedFile(etag: null);

        file.Etag = "new-etag";
        file.UploadedAt = DateTime.UtcNow;
        file.Size = 2048;
        await Storage.UpdateFile(file);

        var updated = await Storage.GetFile(file.Id);
        updated!.Etag.Should().Be("new-etag");
        updated.Size.Should().Be(2048);
    }

    [Fact]
    public async Task AddUploaderToFile_AddsUserId()
    {
        var file = await _helper.SeedFile(uploaders: [1], etag: "etag");

        await Storage.AddUploaderToFile(file.Id, 2);

        var updated = await Storage.GetFile(file.Id);
        updated!.Uploaders.Should().Contain(2);
    }

    [Fact]
    public async Task AddUploaderToFile_DuplicateUserId_DoesNotAddTwice()
    {
        var file = await _helper.SeedFile(uploaders: [1, 2], etag: "etag");

        await Storage.AddUploaderToFile(file.Id, 2);

        var updated = await Storage.GetFile(file.Id);
        updated!.Uploaders.Should().HaveCount(2);
    }

    [Fact]
    public async Task AddUploaderToFile_FileNotFound_DoesNothing()
    {
        await Storage.AddUploaderToFile(Guid.NewGuid(), 1);
    }

    [Fact]
    public async Task GetFiles_ReturnsMatchingFiles()
    {
        var f1 = await _helper.SeedFile();
        var f2 = await _helper.SeedFile();
        await _helper.SeedFile();

        var result = await Storage.GetFiles([f1.Id, f2.Id]);

        result.Should().HaveCount(2);
        result.Select(f => f.Id).Should().Contain([f1.Id, f2.Id]);
    }

    [Fact]
    public async Task DeleteFile_RemovesFile()
    {
        var file = await _helper.SeedFile();

        await Storage.DeleteFile(file.Id);

        var deleted = await Storage.GetFile(file.Id);
        deleted.Should().BeNull();
    }

    [Fact]
    public async Task DeleteFile_NotFound_DoesNothing()
    {
        await Storage.DeleteFile(Guid.NewGuid());
    }

    [Fact]
    public async Task GetFileByPreviewId_ReturnsFile()
    {
        var previewId = Guid.NewGuid();
        await _helper.SeedFile(previewId: previewId);

        var result = await Storage.GetFileByPreviewId(previewId);

        result.Should().NotBeNull();
        result!.PreviewId.Should().Be(previewId);
    }

    [Fact]
    public async Task GetFileByPreviewId_NotFound_ReturnsNull()
    {
        var result = await Storage.GetFileByPreviewId(Guid.NewGuid());
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetUserStorageUsed_ReturnsSumOfUploadedFileSizes()
    {
        await _helper.SeedFile(uploaders: [1], size: 1000, etag: "e1");
        await _helper.SeedFile(uploaders: [1], size: 2000, etag: "e2");
        await _helper.SeedFile(uploaders: [2], size: 5000, etag: "e3");
        await _helper.SeedFile(uploaders: [1], size: 999, etag: null);

        var result = await Storage.GetUserStorageUsed(1);

        result.Should().Be(3000);
    }

    [Fact]
    public async Task GetUserStorageByType_GroupsCorrectly()
    {
        await _helper.SeedFile(uploaders: [1], type: UploadFileType.MessageAttachmentImage, size: 100, etag: "e1");
        await _helper.SeedFile(uploaders: [1], type: UploadFileType.MessageAttachmentImage, size: 200, etag: "e2");
        await _helper.SeedFile(uploaders: [1], type: UploadFileType.MessageAttachmentVideo, size: 500, etag: "e3");

        var result = await Storage.GetUserStorageByType(1);

        result[UploadFileType.MessageAttachmentImage].Should().Be(300);
        result[UploadFileType.MessageAttachmentVideo].Should().Be(500);
    }

    [Fact]
    public async Task ClaimUpload_OnlyOneClaimOwnsTheLease()
    {
        var reservation = await Storage.ReserveUploadAsync(
            1,
            Guid.NewGuid(),
            UploadFileType.MessageAttachmentDocument,
            DateTime.UtcNow,
            TimeSpan.FromHours(2),
            CancellationToken.None);

        var first = await Storage.ClaimUploadAsync(
            reservation.FileId,
            DateTime.UtcNow,
            TimeSpan.FromMinutes(35),
            CancellationToken.None);
        var second = await Storage.ClaimUploadAsync(
            reservation.FileId,
            DateTime.UtcNow,
            TimeSpan.FromMinutes(35),
            CancellationToken.None);

        first.Outcome.Should().Be(UploadClaimOutcome.Claimed);
        first.LeaseToken.Should().NotBeNull();
        second.Outcome.Should().Be(UploadClaimOutcome.Processing);
    }

    [Fact]
    public async Task CompleteUpload_PreservesActualDeduplicatedFileId()
    {
        var reservation = await Storage.ReserveUploadAsync(
            1,
            Guid.NewGuid(),
            UploadFileType.MessageAttachmentDocument,
            DateTime.UtcNow,
            TimeSpan.FromHours(2),
            CancellationToken.None);
        var claim = await Storage.ClaimUploadAsync(
            reservation.FileId,
            DateTime.UtcNow,
            TimeSpan.FromMinutes(35),
            CancellationToken.None);
        var actualFileId = Guid.NewGuid();

        await Storage.CompleteUploadAsync(
            reservation.FileId,
            claim.LeaseToken!.Value,
            actualFileId,
            CancellationToken.None);
        var repeated = await Storage.ClaimUploadAsync(
            reservation.FileId,
            DateTime.UtcNow,
            TimeSpan.FromMinutes(35),
            CancellationToken.None);

        repeated.Outcome.Should().Be(UploadClaimOutcome.Completed);
        repeated.ResultFileId.Should().Be(actualFileId);
    }

    [Fact]
    public async Task ReleaseUpload_ReturnsOperationToPending()
    {
        var reservation = await Storage.ReserveUploadAsync(
            1,
            Guid.NewGuid(),
            UploadFileType.MessageAttachmentDocument,
            DateTime.UtcNow,
            TimeSpan.FromHours(2),
            CancellationToken.None);
        var claim = await Storage.ClaimUploadAsync(
            reservation.FileId,
            DateTime.UtcNow,
            TimeSpan.FromMinutes(35),
            CancellationToken.None);

        await Storage.ReleaseUploadAsync(
            reservation.FileId,
            claim.LeaseToken!.Value,
            CancellationToken.None);
        var repeated = await Storage.ClaimUploadAsync(
            reservation.FileId,
            DateTime.UtcNow,
            TimeSpan.FromMinutes(35),
            CancellationToken.None);

        repeated.Outcome.Should().Be(UploadClaimOutcome.Claimed);
        repeated.LeaseToken.Should().NotBe(claim.LeaseToken!.Value);
    }

    [Fact]
    public async Task DeleteExpiredPending_RemovesPendingOperationAndSlot()
    {
        var reservation = await Storage.ReserveUploadAsync(
            1,
            Guid.NewGuid(),
            UploadFileType.MessageAttachmentDocument,
            DateTime.UtcNow.AddHours(-3),
            TimeSpan.FromHours(2),
            CancellationToken.None);

        var deleted = await Storage.DeleteExpiredPendingAsync();

        deleted.Should().Be(1);
        (await Storage.GetFile(reservation.FileId)).Should().BeNull();
        (await Storage.GetUploadOperationAsync(reservation.FileId)).Should().BeNull();
    }

    [Fact]
    public async Task DeleteExpiredPending_DoesNotDeleteProcessingOperation_AndLeaseCanBeReclaimed()
    {
        var oldNow = DateTime.UtcNow.AddHours(-3);
        var reservation = await Storage.ReserveUploadAsync(
            1,
            Guid.NewGuid(),
            UploadFileType.MessageAttachmentDocument,
            oldNow,
            TimeSpan.FromHours(2),
            CancellationToken.None);
        await Storage.ClaimUploadAsync(
            reservation.FileId,
            oldNow,
            TimeSpan.FromMinutes(35),
            CancellationToken.None);

        var deleted = await Storage.DeleteExpiredPendingAsync();
        var reclaimed = await Storage.ClaimUploadAsync(
            reservation.FileId,
            DateTime.UtcNow,
            TimeSpan.FromMinutes(35),
            CancellationToken.None);

        deleted.Should().Be(0);
        reclaimed.Outcome.Should().Be(UploadClaimOutcome.Claimed);
    }
}
