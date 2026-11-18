using BarkFluff.Files.Features.UploadBadgeImage;
using BarkFluff.Files.Infrastructure;
using BarkFluff.GrpcServer.Settings;
using BarkFluff.Proto.Files;

using Grpc.Core;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

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
        configMock.Setup(config => config["ExternalEndpoint:Host"]).Returns("https://files.example.com");

        _handler = new UploadBadgeImageCommandHandler(
            _helper.BadgeImagesStorage,
            _s3Uploader.Object,
            _bucketRegistry.Object,
            configMock.Object,
            new RunSettings(),
            TestHelper.CreateLogger<UploadBadgeImageCommandHandler>());
    }

    [Theory]
    [InlineData(BadgeImageKind.Full, 256)]
    [InlineData(BadgeImageKind.Mini, 32)]
    public async Task Handle_ValidPngOfExpectedSize_UploadsAndStores(BadgeImageKind kind, int size)
    {
        var bytes = CreatePng(size, size);
        _bucketRegistry.Setup(registry => registry.GetBadgeImageBucketName()).Returns("badge-images");
        _s3Uploader.Setup(uploader => uploader.UploadAsync(
                "badge-images", It.IsAny<string>(), It.IsAny<Stream>(), "image/png", It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-badge");

        var result = await _handler.Handle(new UploadBadgeImageCommand
        {
            ImageData = bytes,
            Filename = "badge-not-trusted.jpg",
            Kind = kind
        }, CancellationToken.None);

        result.BadgeImageId.Should().NotBeNullOrEmpty();
        result.PermanentUrl.Should().Contain("/download/");
        _s3Uploader.Verify(uploader => uploader.UploadAsync(
            "badge-images", It.IsAny<string>(), It.IsAny<Stream>(), "image/png", It.IsAny<CancellationToken>()), Times.Once);

        var stored = _helper.DbContext.BadgeImages.Should().ContainSingle().Subject;
        stored.Filename.Should().Be("badge-not-trusted.jpg");
        stored.Size.Should().Be(bytes.Length);
        stored.Etag.Should().Be("etag-badge");
    }

    [Fact]
    public async Task Handle_NonPngImage_RejectsBeforeS3Write()
    {
        await AssertRejectedBeforeWrite(CreateJpeg(256, 256), BadgeImageKind.Full);
    }

    [Theory]
    [InlineData(BadgeImageKind.Full, 32)]
    [InlineData(BadgeImageKind.Mini, 256)]
    public async Task Handle_WrongDimensionsForKind_RejectsBeforeS3Write(BadgeImageKind kind, int size)
    {
        await AssertRejectedBeforeWrite(CreatePng(size, size), kind);
    }

    [Fact]
    public async Task Handle_UnspecifiedKind_RejectsBeforeS3Write()
    {
        await AssertRejectedBeforeWrite(CreatePng(256, 256), BadgeImageKind.Unspecified);
    }

    [Fact]
    public async Task Handle_ImageOver256Kb_RejectsBeforeS3Write()
    {
        await AssertRejectedBeforeWrite(new byte[256 * 1024 + 1], BadgeImageKind.Full);
    }

    [Fact]
    public async Task Handle_UnknownKind_RejectsBeforeS3Write()
    {
        await AssertRejectedBeforeWrite(CreatePng(256, 256), (BadgeImageKind)99);
    }

    private async Task AssertRejectedBeforeWrite(byte[] imageData, BadgeImageKind kind)
    {
        var act = () => _handler.Handle(new UploadBadgeImageCommand
        {
            ImageData = imageData,
            Filename = "badge.png",
            Kind = kind
        }, CancellationToken.None);

        var exception = await act.Should().ThrowAsync<RpcException>();
        exception.Which.StatusCode.Should().Be(StatusCode.InvalidArgument);
        _s3Uploader.Verify(uploader => uploader.UploadAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _bucketRegistry.Verify(registry => registry.GetBadgeImageBucketName(), Times.Never);
        _helper.DbContext.BadgeImages.Should().BeEmpty();
    }

    private static byte[] CreatePng(int width, int height)
    {
        using var image = new Image<Rgba32>(width, height, new Rgba32(35, 130, 210, 255));
        using var stream = new MemoryStream();
        image.Save(stream, new PngEncoder());
        return stream.ToArray();
    }

    private static byte[] CreateJpeg(int width, int height)
    {
        using var image = new Image<Rgba32>(width, height, new Rgba32(35, 130, 210, 255));
        using var stream = new MemoryStream();
        image.Save(stream, new JpegEncoder());
        return stream.ToArray();
    }
}
