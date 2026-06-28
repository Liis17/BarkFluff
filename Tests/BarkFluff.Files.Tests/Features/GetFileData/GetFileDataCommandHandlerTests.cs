using BarkFluff.Files.Features.GetFileData;
using BarkFluff.GrpcServer.Settings;

namespace BarkFluff.Files.Tests.Features.GetFileData;

public class GetFileDataCommandHandlerTests : IAsyncLifetime
{
    private readonly TestHelper _helper = new();
    private GetFileDataCommandHandler _handler = null!;

    public Task InitializeAsync()
    {
        var config = new Mock<IConfiguration>();
        config.Setup(c => c["ExternalEndpoint:Host"]).Returns("https://example.com");

        _handler = new GetFileDataCommandHandler(
            _helper.UploadedFilesStorage,
            new RunSettings { Http1Port = 7005 },
            config.Object,
            TestHelper.CreateLogger<GetFileDataCommandHandler>());

        return Task.CompletedTask;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Handle_FileFound_ReturnsData()
    {
        var file = await _helper.SeedFile(filename: "photo.jpg", size: 1024);

        var result = await _handler.Handle(new GetFileDataCommand { FileId = file.Id }, CancellationToken.None);

        result.FileInfo.Should().NotBeNull();
        result.FileInfo.Id.Should().Be(file.Id.ToString());
        result.FileInfo.FileName.Should().Be("photo.jpg");
        result.FileInfo.FileSize.Should().Be(1024);
    }

    [Fact]
    public async Task Handle_FileNotFound_ThrowsFileNotFoundException()
    {
        var act = () => _handler.Handle(new GetFileDataCommand { FileId = Guid.NewGuid() }, CancellationToken.None);

        await act.Should().ThrowAsync<FileNotFoundException>();
    }
}
