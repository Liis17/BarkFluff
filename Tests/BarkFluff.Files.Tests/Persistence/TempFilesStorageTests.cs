using BarkFluff.Files.Persistence;

namespace BarkFluff.Files.Tests.Persistence;

public class TempFilesStorageTests : IAsyncLifetime
{
    private readonly TestHelper _helper = new();
    private TempFilesStorage Storage => _helper.TempFilesStorage;

    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task CreateTempFile_SavesWithCorrectOriginalId()
    {
        var fileId = Guid.NewGuid();

        var result = await Storage.CreateTempFile(fileId);

        result.Should().NotBeNull();
        result.OriginalFileId.Should().Be(fileId);
        result.Id.Should().NotBe(Guid.Empty);
        result.ExpiresAt.Should().BeAfter(DateTime.UtcNow);
    }

    [Fact]
    public async Task GetTempFile_FoundNonExpired_ReturnsFile()
    {
        var file = await _helper.SeedFile();
        var temp = await Storage.CreateTempFile(file.Id);

        var result = await Storage.GetTempFile(temp.Id);

        result.Should().NotBeNull();
        result!.OriginalFileId.Should().Be(file.Id);
    }

    [Fact]
    public async Task GetTempFile_Expired_ReturnsNull()
    {
        var file = await _helper.SeedFile();
        var temp = await _helper.SeedTempFile(file.Id, expiresAt: DateTime.UtcNow.AddHours(-1));

        var result = await Storage.GetTempFile(temp.Id);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetTempFile_NotFound_ReturnsNull()
    {
        var result = await Storage.GetTempFile(Guid.NewGuid());
        result.Should().BeNull();
    }

    [Fact]
    public async Task CreateTempFilesBatchAsync_CreatesAll()
    {
        var f1 = await _helper.SeedFile();
        var f2 = await _helper.SeedFile();
        var f3 = await _helper.SeedFile();

        var result = await Storage.CreateTempFilesBatchAsync([f1.Id, f2.Id, f3.Id]);

        result.Should().HaveCount(3);
        result.Select(t => t.OriginalFileId).Should().Contain([f1.Id, f2.Id, f3.Id]);
    }

    [Fact]
    public async Task CreateTempFilesBatchAsync_EmptyList_ReturnsEmpty()
    {
        var result = await Storage.CreateTempFilesBatchAsync([]);
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteExpiredAsync_RemovesOnlyExpired()
    {
        var f1 = await _helper.SeedFile();
        var f2 = await _helper.SeedFile();
        await _helper.SeedTempFile(f1.Id, expiresAt: DateTime.UtcNow.AddHours(-1));
        var valid = await _helper.SeedTempFile(f2.Id, expiresAt: DateTime.UtcNow.AddHours(1));

        var deleted = await Storage.DeleteExpiredAsync();

        deleted.Should().Be(1);
        (await Storage.GetTempFile(valid.Id)).Should().NotBeNull();
    }
}
