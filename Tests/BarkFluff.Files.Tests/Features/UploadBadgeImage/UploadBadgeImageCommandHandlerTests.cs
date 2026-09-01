using BarkFluff.Files.Features.UploadBadgeImage;
using BarkFluff.Files.Infrastructure;
using BarkFluff.GrpcServer.Settings;

namespace BarkFluff.Files.Tests.Features.UploadBadgeImage;

public class UploadBadgeImageCommandHandlerTests
{
    private readonly TestHelper _helper = new();
    private readonly Mock<IS3Uploader> _s3Uploader;
    private readonly Mock<IS3BucketRegistry> _bucketRegistry;
    private readonly UploadBadgeImageCommandHandler _handler;

    public UploadBadgeImageCommandHandlerTests()
    {
        _s3Uploader = _helper.S3UploaderMock;
        _bucketRegistry = _helper.S3BucketRegistryMock;

        var configMock = new Mock<IConfiguration>();
        configMock.Setup(c => c["ExternalEndpoint:Host"]).Returns("https://files.example.com");

        _handler = new UploadBadgeImageCommandHandler(
            _helper.BadgeImagesStorage,
            _s3Uploader.Object,
            _bucketRegistry.Object,
            configMock.Object,
            new RunSettings(),
            TestHelper.CreateLogger<UploadBadgeImageCommandHandler>()
        );
    }

    [Fact]
    public async Task Handle_UploadsBadgeImageToS3()
    {
        var imageData = new byte[] { 1, 2, 3, 4 };
        _bucketRegistry.Setup(r => r.GetBadgeImageBucketName()).Returns("badge-images");
        _s3Uploader.Setup(u => u.UploadAsync("badge-images", It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-badge");

        var result = await _handler.Handle(new UploadBadgeImageCommand
        {
            ImageData = imageData,
            Filename = "badge.png"
        }, CancellationToken.None);

        result.Should().NotBeNull();
        result.BadgeImageId.Should().NotBeNullOrEmpty();
        result.PermanentUrl.Should().Contain("/download/");
    }

    [Fact]
    public async Task Handle_SavesBadgeImageToDb()
    {
        var imageData = new byte[] { 1, 2, 3, 4 };
        _bucketRegistry.Setup(r => r.GetBadgeImageBucketName()).Returns("badge-images");
        _s3Uploader.Setup(u => u.UploadAsync("badge-images", It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-badge");

        await _handler.Handle(new UploadBadgeImageCommand
        {
            ImageData = imageData,
            Filename = "badge.png"
        }, CancellationToken.None);

        var badge = _helper.DbContext.BadgeImages.Single();
        badge.Filename.Should().Be("badge.png");
        badge.Size.Should().Be(4);
        badge.Etag.Should().Be("etag-badge");
        badge.UploadedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Handle_UsesBadgeBucket()
    {
        var imageData = new byte[] { 1, 2, 3 };
        _bucketRegistry.Setup(r => r.GetBadgeImageBucketName()).Returns("badge-images");
        _s3Uploader.Setup(u => u.UploadAsync("badge-images", It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");

        await _handler.Handle(new UploadBadgeImageCommand
        {
            ImageData = imageData,
            Filename = "badge.png"
        }, CancellationToken.None);

        _bucketRegistry.Verify(r => r.GetBadgeImageBucketName(), Times.Once());
        _s3Uploader.Verify(
            u => u.UploadAsync("badge-images", It.IsAny<string>(), It.IsAny<Stream>(), "image/png", It.IsAny<CancellationToken>()),
            Times.Once());
    }

    [Fact]
    public async Task Handle_JpegBadge_UsesJpegContentType()
    {
        var imageData = new byte[] { 1, 2, 3 };
        _bucketRegistry.Setup(r => r.GetBadgeImageBucketName()).Returns("badge-images");
        _s3Uploader.Setup(u => u.UploadAsync("badge-images", It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");

        await _handler.Handle(new UploadBadgeImageCommand
        {
            ImageData = imageData,
            Filename = "badge.jpg"
        }, CancellationToken.None);

        _s3Uploader.Verify(
            u => u.UploadAsync("badge-images", It.IsAny<string>(), It.IsAny<Stream>(), "image/jpeg", It.IsAny<CancellationToken>()),
            Times.Once());
    }
}
