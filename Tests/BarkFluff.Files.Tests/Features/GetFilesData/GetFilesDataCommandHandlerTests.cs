using BarkFluff.Files.Features.GetFilesData;
using BarkFluff.GrpcServer.Settings;

namespace BarkFluff.Files.Tests.Features.GetFilesData;

public class GetFilesDataCommandHandlerTests : IAsyncLifetime
{
    private readonly TestHelper _helper = new();
    private GetFilesDataCommandHandler _handler = null!;

    public Task InitializeAsync()
    {
        var config = new Mock<IConfiguration>();
        config.Setup(c => c["ExternalEndpoint:Host"]).Returns("https://example.com");

        _handler = new GetFilesDataCommandHandler(
            _helper.UploadedFilesStorage,
            new RunSettings { Http1Port = 7005 },
            config.Object,
            TestHelper.CreateLogger<GetFilesDataCommandHandler>());

        return Task.CompletedTask;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Handle_MultipleFiles_ReturnsAll()
    {
        var f1 = await _helper.SeedFile(filename: "a.jpg");
        var f2 = await _helper.SeedFile(filename: "b.mp4");

        var result = await _handler.Handle(
            new GetFilesDataCommand { FileIds = [f1.Id, f2.Id] }, CancellationToken.None);

        result.FilesInfos.Should().HaveCount(2);
    }

    [Fact]
    public async Task Handle_SomeMissing_ReturnsOnlyFound()
    {
        var f1 = await _helper.SeedFile();

        var result = await _handler.Handle(
            new GetFilesDataCommand { FileIds = [f1.Id, Guid.NewGuid()] }, CancellationToken.None);

        result.FilesInfos.Should().HaveCount(1);
    }

    [Fact]
    public async Task Handle_EmptyList_ReturnsEmpty()
    {
        var result = await _handler.Handle(
            new GetFilesDataCommand { FileIds = [] }, CancellationToken.None);

        result.FilesInfos.Should().BeEmpty();
    }
}
