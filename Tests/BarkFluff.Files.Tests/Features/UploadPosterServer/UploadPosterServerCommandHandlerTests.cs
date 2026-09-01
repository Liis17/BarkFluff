using BarkFluff.Files.Features.UploadPosterServer;
using BarkFluff.Files.Infrastructure;
using BarkFluff.GrpcServer.Settings;

using BarkFluff.Files.Domain;

namespace BarkFluff.Files.Tests.Features.UploadPosterServer;

public class UploadPosterServerCommandHandlerTests
{
    private readonly TestHelper _helper = new();
    private readonly Mock<IS3Uploader> _s3Uploader;
    private readonly Mock<IS3BucketRegistry> _bucketRegistry;
    private readonly UploadPosterServerCommandHandler _handler;

    public UploadPosterServerCommandHandlerTests()
    {
        _s3Uploader = _helper.S3UploaderMock;
        _bucketRegistry = _helper.S3BucketRegistryMock;

        var configMock = new Mock<IConfiguration>();
        configMock.Setup(c => c["ExternalEndpoint:Host"]).Returns("https://files.example.com");

        _handler = new UploadPosterServerCommandHandler(
            _helper.UploadedFilesStorage,
            _s3Uploader.Object,
            _bucketRegistry.Object,
            _helper.ImageCompressor,
            configMock.Object,
            new RunSettings(),
            TestHelper.CreateLogger<UploadPosterServerCommandHandler>()
        );
    }

    private static byte[] CreateTestImageBytes()
    {
        using var image = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(100, 100);
        using var ms = new MemoryStream();
        image.Save(ms, new JpegEncoder());
        return ms.ToArray();
    }

    [Fact]
    public async Task Handle_UploadsFileToS3()
    {
        var imageData = CreateTestImageBytes();
        _bucketRegistry.Setup(r => r.GetBucketName(UploadFileType.UserProfilePoster)).Returns("profile-pictures");
        _s3Uploader.Setup(u => u.UploadAsync("profile-pictures", It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-poster");

        var result = await _handler.Handle(new UploadPosterServerCommand
        {
            UserId = 10,
            ImageData = imageData,
            Filename = "poster.jpg"
        }, CancellationToken.None);

        result.Should().NotBeNull();
        result.FileUrl.Should().Contain("/download/");
        result.FileId.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Handle_CreatesFileInDb()
    {
        var imageData = CreateTestImageBytes();
        _bucketRegistry.Setup(r => r.GetBucketName(UploadFileType.UserProfilePoster)).Returns("profile-pictures");
        _s3Uploader.Setup(u => u.UploadAsync("profile-pictures", It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-poster");

        await _handler.Handle(new UploadPosterServerCommand
        {
            UserId = 10,
            ImageData = imageData,
            Filename = "poster.jpg"
        }, CancellationToken.None);

        var file = _helper.DbContext.UploadedFiles.Single();
        file.Type.Should().Be(UploadFileType.UserProfilePoster);
        file.Uploaders.Should().ContainSingle(x => x == 10);
        file.Etag.Should().Be("etag-poster");
    }

    [Fact]
    public async Task Handle_SingleUploadCall()
    {
        var imageData = CreateTestImageBytes();
        _bucketRegistry.Setup(r => r.GetBucketName(UploadFileType.UserProfilePoster)).Returns("profile-pictures");
        _s3Uploader.Setup(u => u.UploadAsync("profile-pictures", It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-poster");

        await _handler.Handle(new UploadPosterServerCommand
        {
            UserId = 1,
            ImageData = imageData,
            Filename = "poster.jpg"
        }, CancellationToken.None);

        _s3Uploader.Verify(
            u => u.UploadAsync("profile-pictures", It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once());
    }
}
