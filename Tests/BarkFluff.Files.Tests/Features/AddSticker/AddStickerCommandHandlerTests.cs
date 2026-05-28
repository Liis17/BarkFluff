using BarkFluff.Files.Features.AddSticker;
using BarkFluff.Files.Infrastructure;
using BarkFluff.GrpcServer.Settings;

using BarkFluff.Files.Domain;

namespace BarkFluff.Files.Tests.Features.AddSticker;

public class AddStickerCommandHandlerTests
{
    private readonly TestHelper _helper = new();
    private readonly Mock<IS3Uploader> _s3Uploader;
    private readonly Mock<IS3BucketRegistry> _bucketRegistry;
    private readonly AddStickerCommandHandler _handler;

    public AddStickerCommandHandlerTests()
    {
        _s3Uploader = _helper.S3UploaderMock;
        _bucketRegistry = _helper.S3BucketRegistryMock;

        var configMock = new Mock<IConfiguration>();
        configMock.Setup(c => c["ExternalEndpoint:Host"]).Returns("https://files.example.com");

        _handler = new AddStickerCommandHandler(
            _helper.StickersStorage,
            _helper.StickerPacksStorage,
            _helper.UploadedFilesStorage,
            _s3Uploader.Object,
            _bucketRegistry.Object,
            _helper.ImageCompressor,
            configMock.Object,
            new RunSettings(),
            TestHelper.CreateLogger<AddStickerCommandHandler>()
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
    public async Task Handle_PackNotFound_Throws()
    {
        var command = new AddStickerCommand
        {
            PackId = Guid.NewGuid(),
            FileId = Guid.NewGuid(),
            Emoji = "😀"
        };

        var act = () => _handler.Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<Exception>().WithMessage("Стикерпак не найден");
    }

    [Fact]
    public async Task Handle_FileNotFound_Throws()
    {
        var pack = await _helper.SeedStickerPack();

        var command = new AddStickerCommand
        {
            PackId = pack.Id,
            FileId = Guid.NewGuid(),
            Emoji = "😀"
        };

        var act = () => _handler.Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<Exception>().WithMessage("Файл не найден");
    }

    [Fact]
    public async Task Handle_WrongFileType_Throws()
    {
        var pack = await _helper.SeedStickerPack();
        var file = await _helper.SeedFile(type: UploadFileType.MessageAttachmentImage, etag: "etag");

        var command = new AddStickerCommand
        {
            PackId = pack.Id,
            FileId = file.Id,
            Emoji = "😀"
        };

        var act = () => _handler.Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<Exception>().WithMessage("Файл не является стикером*");
    }

    [Fact]
    public async Task Handle_FileNotUploaded_Throws()
    {
        var pack = await _helper.SeedStickerPack();
        var file = await _helper.SeedFile(type: UploadFileType.MessageAttachmentSticker, etag: null);

        var command = new AddStickerCommand
        {
            PackId = pack.Id,
            FileId = file.Id,
            Emoji = "😀"
        };

        var act = () => _handler.Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<Exception>().WithMessage("*не загружен*");
    }

    [Fact]
    public async Task Handle_ValidSticker_CreatesStickerAndPreview()
    {
        var pack = await _helper.SeedStickerPack();
        var imageData = CreateTestImageBytes();
        var file = await _helper.SeedFile(type: UploadFileType.MessageAttachmentSticker, etag: "etag-sticker");

        _bucketRegistry.Setup(r => r.GetBucketName(UploadFileType.MessageAttachmentSticker)).Returns("message-documents");
        _s3Uploader.Setup(u => u.DownloadAsync("message-documents", $"{file.Id}"))
            .ReturnsAsync(new MemoryStream(imageData));
        _s3Uploader.Setup(u => u.UploadAsync("message-documents", It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>()))
            .ReturnsAsync("etag-preview");

        var result = await _handler.Handle(new AddStickerCommand
        {
            PackId = pack.Id,
            FileId = file.Id,
            Emoji = "😀"
        }, CancellationToken.None);

        result.Should().NotBeNull();
        result.Sticker.Should().NotBeNull();

        var sticker = _helper.DbContext.Stickers.Single();
        sticker.StickerPackId.Should().Be(pack.Id);
        sticker.FileId.Should().Be(file.Id);
        sticker.PreviewFileId.Should().NotBeNull();
        sticker.Emoji.Should().Be("😀");

        var previewFiles = _helper.DbContext.UploadedFiles.Where(f => f.Id == sticker.PreviewFileId).ToList();
        previewFiles.Should().HaveCount(1);
    }

    [Fact]
    public async Task Handle_DownloadsOriginalAndUploadsPreview()
    {
        var pack = await _helper.SeedStickerPack();
        var imageData = CreateTestImageBytes();
        var file = await _helper.SeedFile(type: UploadFileType.MessageAttachmentSticker, etag: "etag");

        _bucketRegistry.Setup(r => r.GetBucketName(UploadFileType.MessageAttachmentSticker)).Returns("message-documents");
        _s3Uploader.Setup(u => u.DownloadAsync("message-documents", $"{file.Id}"))
            .ReturnsAsync(new MemoryStream(imageData));
        _s3Uploader.Setup(u => u.UploadAsync("message-documents", It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>()))
            .ReturnsAsync("etag-preview");

        await _handler.Handle(new AddStickerCommand
        {
            PackId = pack.Id,
            FileId = file.Id,
            Emoji = "😎"
        }, CancellationToken.None);

        _s3Uploader.Verify(u => u.DownloadAsync("message-documents", $"{file.Id}"), Times.Once());
        _s3Uploader.Verify(
            u => u.UploadAsync("message-documents", It.IsAny<string>(), It.IsAny<Stream>(), "image/webp"),
            Times.Once());
    }
}
