using BarkFluff.Files.Features.CheckFileHash;
using BarkFluff.Files.Infrastructure;

namespace BarkFluff.Files.Tests.Features.CheckFileHash;

public class CheckFileHashCommandHandlerTests : IAsyncLifetime
{
    private readonly TestHelper _helper = new();
    private CheckFileHashCommandHandler _handler = null!;

    public Task InitializeAsync()
    {
        _handler = new CheckFileHashCommandHandler(
            _helper.FileHashesStorage,
            _helper.UploadedFilesStorage,
            _helper.CreateUserContext(42),
            TestHelper.CreateLogger<CheckFileHashCommandHandler>());

        return Task.CompletedTask;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Handle_ValidHashExistingFile_ReturnsFileId()
    {
        var fileId = Guid.NewGuid();
        var hash = new string('a', 64);
        await _helper.SeedFileHash(fileId, hash);
        await _helper.SeedFile(id: fileId, etag: "etag");

        var result = await _handler.Handle(new CheckFileHashCommand { FileHash = hash }, CancellationToken.None);

        result.FileId.Should().Be(fileId.ToString());
    }

    [Fact]
    public async Task Handle_ValidHashExistingFile_AddsCurrentUserAsUploader()
    {
        var fileId = Guid.NewGuid();
        var hash = new string('b', 64);
        await _helper.SeedFileHash(fileId, hash);
        await _helper.SeedFile(id: fileId, etag: "etag", uploaders: [1]);

        await _handler.Handle(new CheckFileHashCommand { FileHash = hash }, CancellationToken.None);

        var file = await _helper.UploadedFilesStorage.GetFile(fileId);
        file!.Uploaders.Should().Contain(42);
    }

    [Fact]
    public async Task Handle_ValidHashNoFile_ReturnsEmpty()
    {
        var hash = new string('c', 64);

        var result = await _handler.Handle(new CheckFileHashCommand { FileHash = hash }, CancellationToken.None);

        result.FileId.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_EmptyHash_ReturnsEmpty()
    {
        var result = await _handler.Handle(new CheckFileHashCommand { FileHash = "" }, CancellationToken.None);

        result.FileId.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_NullHash_ReturnsEmpty()
    {
        var result = await _handler.Handle(new CheckFileHashCommand { FileHash = null! }, CancellationToken.None);

        result.FileId.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_ShortHash_ReturnsEmpty()
    {
        var result = await _handler.Handle(new CheckFileHashCommand { FileHash = "abc123" }, CancellationToken.None);

        result.FileId.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_InvalidHexChars_ReturnsEmpty()
    {
        var result = await _handler.Handle(new CheckFileHashCommand { FileHash = new string('g', 64) }, CancellationToken.None);

        result.FileId.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_UppercaseHash_FindsLowercaseMatch()
    {
        var fileId = Guid.NewGuid();
        var lowerHash = new string('a', 64);
        var upperHash = new string('A', 64);
        await _helper.SeedFileHash(fileId, lowerHash);
        await _helper.SeedFile(id: fileId, etag: "etag");

        var result = await _handler.Handle(new CheckFileHashCommand { FileHash = upperHash }, CancellationToken.None);

        result.FileId.Should().Be(fileId.ToString());
    }
}
