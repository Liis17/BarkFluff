using BarkFluff.Files.Domain;
using BarkFluff.Files.Persistence;

namespace BarkFluff.Files.Tests.Persistence;

public class FileHashesStorageTests : IAsyncLifetime
{
    private readonly TestHelper _helper = new();
    private FileHashesStorage Storage => _helper.FileHashesStorage;

    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task AddHash_SavesHash()
    {
        var fileId = Guid.NewGuid();
        var hash = "abc123";

        await Storage.AddHash(new Domain.FileHash { FileId = fileId, Hash = hash });

        var result = await Storage.GetFileIdByHash(hash);
        result.Should().Be(fileId);
    }

    [Fact]
    public async Task GetFileIdByHash_NotFound_ReturnsNull()
    {
        var result = await Storage.GetFileIdByHash("nonexistent");
        result.Should().BeNull();
    }

    [Fact]
    public async Task HashExists_Found_ReturnsTrue()
    {
        var hash = "abc123";
        await Storage.AddHash(new Domain.FileHash { FileId = Guid.NewGuid(), Hash = hash });

        var result = await Storage.HashExists(hash);
        result.Should().BeTrue();
    }

    [Fact]
    public async Task HashExists_NotFound_ReturnsFalse()
    {
        var result = await Storage.HashExists("nonexistent");
        result.Should().BeFalse();
    }

    [Fact]
    public async Task GetHashByFileId_ReturnsHash()
    {
        var fileId = Guid.NewGuid();
        var hash = "abc123";
        await Storage.AddHash(new Domain.FileHash { FileId = fileId, Hash = hash });

        var result = await Storage.GetHashByFileId(fileId);
        result.Should().NotBeNull();
        result!.Hash.Should().Be(hash);
    }

    [Fact(Skip = "InMemory EF Core does not support ExecuteDeleteAsync")]
    public void DeleteHashByFileId_RemovesHash() { }

    [Fact(Skip = "InMemory EF Core does not support ExecuteDeleteAsync")]
    public void DeleteHashByFileId_NotFound_ReturnsZero() { }
}
