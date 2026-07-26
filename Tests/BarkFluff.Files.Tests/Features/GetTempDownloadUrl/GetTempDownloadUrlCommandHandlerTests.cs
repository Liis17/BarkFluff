using BarkFluff.Files.Features.GetTempDownloadUrl;
using BarkFluff.GrpcServer.Settings;

namespace BarkFluff.Files.Tests.Features.GetTempDownloadUrl;

public class GetTempDownloadUrlCommandHandlerTests : IAsyncLifetime
{
    private readonly TestHelper _helper = new();
    private GetTempDownloadUrlCommandHandler _handler = null!;

    public Task InitializeAsync()
    {
        var config = new Mock<IConfiguration>();
        config.Setup(c => c["ExternalEndpoint:Host"]).Returns("https://example.com");

        _handler = new GetTempDownloadUrlCommandHandler(
            _helper.UploadedFilesStorage,
            _helper.TempFilesStorage,
            Mock.Of<BarkFluff.Proto.Messages.MessagesServerApi.MessagesServerApiClient>(),
            new RunSettings { Http1Port = 7005 },
            config.Object,
            TestHelper.CreateLogger<GetTempDownloadUrlCommandHandler>());

        return Task.CompletedTask;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Handle_AllFilesFound_ReturnsUrls()
    {
        var f1 = await _helper.SeedFile(etag: "e1");
        var f2 = await _helper.SeedFile(etag: "e2");

        var result = await _handler.Handle(
            new GetTempDownloadUrlCommand { FileIds = [f1.Id, f2.Id] }, CancellationToken.None);

        result.FileUrls.Should().HaveCount(2);
        result.FileUrls.Select(u => u.Url).Should().OnlyContain(u => u.Contains("/download/"));
    }

    [Fact]
    public async Task Handle_FileUrlsMapToOriginalFileIds()
    {
        var f1 = await _helper.SeedFile(etag: "e1");

        var result = await _handler.Handle(
            new GetTempDownloadUrlCommand { FileIds = [f1.Id] }, CancellationToken.None);

        result.FileUrls[0].FileId.Should().Be(f1.Id.ToString());
    }

    [Fact]
    public async Task Handle_NoFilesFound_ReturnsEmptyFileUrls()
    {
        var result = await _handler.Handle(
            new GetTempDownloadUrlCommand { FileIds = [Guid.NewGuid()] }, CancellationToken.None);

        result.FileUrls.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_SomeFilesMissing_StillReturnsFoundOnes()
    {
        var f1 = await _helper.SeedFile(etag: "e1");
        var missingId = Guid.NewGuid();

        var result = await _handler.Handle(
            new GetTempDownloadUrlCommand { FileIds = [f1.Id, missingId] }, CancellationToken.None);

        result.FileUrls.Should().HaveCount(1);
        result.FileUrls[0].FileId.Should().Be(f1.Id.ToString());
    }

    [Fact]
    public async Task Handle_EmptyList_ReturnsEmptyResponse()
    {
        var result = await _handler.Handle(
            new GetTempDownloadUrlCommand { FileIds = [] }, CancellationToken.None);

        result.FileUrls.Should().BeEmpty();
    }
}
