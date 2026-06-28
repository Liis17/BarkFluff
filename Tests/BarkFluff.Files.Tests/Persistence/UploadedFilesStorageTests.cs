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
}
