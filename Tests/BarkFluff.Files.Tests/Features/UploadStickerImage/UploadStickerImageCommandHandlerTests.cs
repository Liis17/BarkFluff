using BarkFluff.Files.Features.UploadStickerImage;
using BarkFluff.Files.Infrastructure;
using BarkFluff.GrpcServer.Settings;

using BarkFluff.Files.Domain;

namespace BarkFluff.Files.Tests.Features.UploadStickerImage;

public class UploadStickerImageCommandHandlerTests
{
    private readonly TestHelper _helper = new();
    private readonly Mock<IS3Uploader> _s3Uploader;
    private readonly Mock<IS3BucketRegistry> _bucketRegistry;
    private readonly UploadStickerImageCommandHandler _handler;

    public UploadStickerImageCommandHandlerTests()
    {
        _s3Uploader = _helper.S3UploaderMock;
        _bucketRegistry = _helper.S3BucketRegistryMock;

        var configMock = new Mock<IConfiguration>();
        configMock.Setup(c => c["ExternalEndpoint:Host"]).Returns("https://files.example.com");

        _handler = new UploadStickerImageCommandHandler(
            _helper.UploadedFilesStorage,
            _s3Uploader.Object,
            _bucketRegistry.Object,
            _helper.ImageCompressor,
            configMock.Object,
            new RunSettings(),
            TestHelper.CreateLogger<UploadStickerImageCommandHandler>()
        );
    }

    private static byte[] CreateTestImageBytes()
    {
        using var image = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(512, 512);
        using var ms = new MemoryStream();
        image.Save(ms, new PngEncoder());
        return ms.ToArray();
    }

    [Fact]
    public async Task Handle_UploadsStickerImageToS3()
    {
        var imageData = CreateTestImageBytes();
        _bucketRegistry.Setup(r => r.GetBucketName(UploadFileType.MessageAttachmentSticker)).Returns("message-documents");
        _s3Uploader.Setup(u => u.UploadAsync("message-documents", It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>()))
            .ReturnsAsync("etag-sticker");

        var result = await _handler.Handle(new UploadStickerImageCommand
        {
            ImageData = imageData,
            Filename = "sticker.png"
        }, CancellationToken.None);

        result.Should().NotBeNull();
        result.FileId.Should().NotBeNullOrEmpty();
        result.FileUrl.Should().Contain("/download/");
    }

    [Fact]
    public async Task Handle_SavesStickerFileInDb()
    {
        var imageData = CreateTestImageBytes();
        _bucketRegistry.Setup(r => r.GetBucketName(UploadFileType.MessageAttachmentSticker)).Returns("message-documents");
        _s3Uploader.Setup(u => u.UploadAsync("message-documents", It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>()))
            .ReturnsAsync("etag-sticker");

        await _handler.Handle(new UploadStickerImageCommand
        {
            ImageData = imageData,
            Filename = "sticker.png"
        }, CancellationToken.None);

        var file = _helper.DbContext.UploadedFiles.Single();
        file.Type.Should().Be(UploadFileType.MessageAttachmentSticker);
        file.Etag.Should().Be("etag-sticker");
        file.Uploaders.Should().ContainSingle(x => x == 0);
    }

    [Fact]
    public async Task Handle_UploadsAsWebP()
    {
        var imageData = CreateTestImageBytes();
        _bucketRegistry.Setup(r => r.GetBucketName(UploadFileType.MessageAttachmentSticker)).Returns("message-documents");
        _s3Uploader.Setup(u => u.UploadAsync("message-documents", It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>()))
            .ReturnsAsync("etag-sticker");

        await _handler.Handle(new UploadStickerImageCommand
        {
            ImageData = imageData,
            Filename = "sticker.png"
        }, CancellationToken.None);

        _s3Uploader.Verify(
            u => u.UploadAsync("message-documents", It.IsAny<string>(), It.IsAny<Stream>(), "image/webp"),
            Times.Once());
    }
}
