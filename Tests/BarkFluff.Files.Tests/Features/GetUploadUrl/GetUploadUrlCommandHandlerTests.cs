using BarkFluff.Files.Features.GetUploadUrl;
using BarkFluff.GrpcServer.Settings;

namespace BarkFluff.Files.Tests.Features.GetUploadUrl;

public class GetUploadUrlCommandHandlerTests : IAsyncLifetime
{
    private readonly TestHelper _helper = new();
    private GetUploadUrlCommandHandler _handler = null!;

    public Task InitializeAsync()
    {
        var config = new Mock<IConfiguration>();
        config.Setup(c => c["ExternalEndpoint:Host"]).Returns("https://example.com");

        _handler = new GetUploadUrlCommandHandler(
            _helper.UploadedFilesStorage,
            _helper.CreateUserContext(1),
            new RunSettings { Http1Port = 7005 },
            config.Object,
            TestHelper.CreateLogger<GetUploadUrlCommandHandler>());

        return Task.CompletedTask;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Handle_CreatesFileAndReturnsUrl()
    {
        var command = new GetUploadUrlCommand { Type = Domain.UploadFileType.MessageAttachmentImage };

        var result = await _handler.Handle(command, CancellationToken.None);

        result.Url.Should().Contain("/upload/");
        result.FileId.Should().NotBeEmpty();
        Guid.Parse(result.FileId).Should().NotBe(Guid.Empty);
    }

    [Fact]
    public async Task Handle_StoresFileWithCurrentUserAsUploader()
    {
        var command = new GetUploadUrlCommand { Type = Domain.UploadFileType.UserAvatar };

        var result = await _handler.Handle(command, CancellationToken.None);

        var fileId = Guid.Parse(result.FileId);
        var file = await _helper.UploadedFilesStorage.GetFile(fileId);
        file!.Uploaders.Should().Contain(1);
        file.Type.Should().Be(Domain.UploadFileType.UserAvatar);
    }

    [Fact]
    public async Task Handle_StoresFileWithCorrectType()
    {
        var command = new GetUploadUrlCommand { Type = Domain.UploadFileType.MessageAttachmentVideo };

        var result = await _handler.Handle(command, CancellationToken.None);

        var fileId = Guid.Parse(result.FileId);
        var file = await _helper.UploadedFilesStorage.GetFile(fileId);
        file!.Type.Should().Be(Domain.UploadFileType.MessageAttachmentVideo);
    }
}
