using BarkFluff.Files.Features.UploadAvatarServer;
using BarkFluff.Files.Infrastructure;
using BarkFluff.GrpcServer.Settings;

using BarkFluff.Files.Domain;

namespace BarkFluff.Files.Tests.Features.UploadAvatarServer;

public class UploadAvatarServerCommandHandlerTests
{
    private readonly TestHelper _helper = new();
    private readonly Mock<IS3Uploader> _s3Uploader;
    private readonly Mock<IS3BucketRegistry> _bucketRegistry;
    private readonly UploadAvatarServerCommandHandler _handler;

    public UploadAvatarServerCommandHandlerTests()
    {
        _s3Uploader = _helper.S3UploaderMock;
        _bucketRegistry = _helper.S3BucketRegistryMock;

        var configMock = new Mock<IConfiguration>();
        configMock.Setup(c => c["ExternalEndpoint:Host"]).Returns("https://files.example.com");

        _handler = new UploadAvatarServerCommandHandler(
            _helper.UploadedFilesStorage,
            _s3Uploader.Object,
            _bucketRegistry.Object,
            _helper.ImageCompressor,
            configMock.Object,
            new RunSettings(),
            TestHelper.CreateLogger<UploadAvatarServerCommandHandler>()
        );
    }

    private static byte[] CreateTestImageBytes()
    {
        using var image = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(64, 64);
        using var ms = new MemoryStream();
        image.Save(ms, new JpegEncoder());
        return ms.ToArray();
    }

    [Fact]
    public async Task Handle_UploadsMainAndPreviewToS3()
    {
        var imageData = CreateTestImageBytes();
        _bucketRegistry.Setup(r => r.GetBucketName(UploadFileType.UserAvatar)).Returns("profile-pictures");
        _s3Uploader.Setup(u => u.UploadAsync("profile-pictures", It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>()))
            .ReturnsAsync((string _, string _, Stream _, string _) => "etag-test");

        var command = new UploadAvatarServerCommand
        {
            UserId = 42,
            ImageData = imageData,
            Filename = "avatar.jpg"
        };

        var result = await _handler.Handle(command, CancellationToken.None);

        result.Should().NotBeNull();
        result.FileUrl.Should().Contain("/download/");
        result.PreviewUrl.Should().Contain("/download/");
        result.FileId.Should().NotBeNullOrEmpty();

        _s3Uploader.Verify(
            u => u.UploadAsync("profile-pictures", It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task Handle_CreatesTwoFilesInDb()
    {
        var imageData = CreateTestImageBytes();
        _bucketRegistry.Setup(r => r.GetBucketName(UploadFileType.UserAvatar)).Returns("profile-pictures");
        _s3Uploader.Setup(u => u.UploadAsync("profile-pictures", It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>()))
            .ReturnsAsync("etag-test");

        await _handler.Handle(new UploadAvatarServerCommand
        {
            UserId = 1,
            ImageData = imageData,
            Filename = "avatar.jpg"
        }, CancellationToken.None);

        _helper.DbContext.UploadedFiles.Count().Should().Be(2);
    }

    [Fact]
    public async Task Handle_MainFileHasPreviewId()
    {
        var imageData = CreateTestImageBytes();
        _bucketRegistry.Setup(r => r.GetBucketName(UploadFileType.UserAvatar)).Returns("profile-pictures");
        _s3Uploader.Setup(u => u.UploadAsync("profile-pictures", It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>()))
            .ReturnsAsync("etag-test");

        await _handler.Handle(new UploadAvatarServerCommand
        {
            UserId = 1,
            ImageData = imageData,
            Filename = "avatar.jpg"
        }, CancellationToken.None);

        var files = _helper.DbContext.UploadedFiles.ToList();
        var mainFile = files.FirstOrDefault(f => f.PreviewId.HasValue);
        mainFile.Should().NotBeNull();
        mainFile!.Type.Should().Be(UploadFileType.UserAvatar);
        mainFile.Uploaders.Should().ContainSingle(x => x == 1);
    }

    [Fact]
    public async Task Handle_PreviewFileHasNoPreviewId()
    {
        var imageData = CreateTestImageBytes();
        _bucketRegistry.Setup(r => r.GetBucketName(UploadFileType.UserAvatar)).Returns("profile-pictures");
        _s3Uploader.Setup(u => u.UploadAsync("profile-pictures", It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>()))
            .ReturnsAsync("etag-test");

        await _handler.Handle(new UploadAvatarServerCommand
        {
            UserId = 1,
            ImageData = imageData,
            Filename = "avatar.jpg"
        }, CancellationToken.None);

        var files = _helper.DbContext.UploadedFiles.ToList();
        var previewFile = files.FirstOrDefault(f => !f.PreviewId.HasValue);
        previewFile.Should().NotBeNull();
        previewFile!.Etag.Should().Be("etag-test");
    }
}
