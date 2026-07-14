using BarkFluff.Files.Exceptions;
using BarkFluff.Files.Features.UploadFile;
using BarkFluff.Files.Infrastructure;
using BarkFluff.Files.Services;

using BarkFluff.Files.Domain;

namespace BarkFluff.Files.Tests.Features.UploadFile;

public class UploadFileCommandHandlerTests
{
    private readonly TestHelper _helper = new();
    private readonly Mock<IS3Uploader> _s3Uploader;
    private readonly Mock<IS3BucketRegistry> _bucketRegistry;
    private readonly UploadFileCommandHandler _handler;

    public UploadFileCommandHandlerTests()
    {
        _s3Uploader = _helper.S3UploaderMock;
        _bucketRegistry = _helper.S3BucketRegistryMock;

        _handler = new UploadFileCommandHandler(
            _helper.UploadedFilesStorage,
            _helper.FileHashesStorage,
            _s3Uploader.Object,
            _bucketRegistry.Object,
            _helper.ImageCompressor,
            _helper.FileTypeDetector,
            _helper.VideoThumbnailExtractor,
            TestHelper.CreateLogger<UploadFileCommandHandler>()
        );
    }

    private static Stream CreateJpegStream(int width = 100, int height = 100)
    {
        using var image = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(width, height);
        var ms = new MemoryStream();
        image.Save(ms, new JpegEncoder());
        ms.Position = 0;
        return ms;
    }

    private static Stream CreatePngStream(int width = 100, int height = 100)
    {
        using var image = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(width, height);
        var ms = new MemoryStream();
        image.Save(ms, new PngEncoder());
        ms.Position = 0;
        return ms;
    }

    private static Stream CreateBmpStream(int width = 100, int height = 100)
    {
        using var image = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(width, height);
        var ms = new MemoryStream();
        image.Save(ms, new SixLabors.ImageSharp.Formats.Bmp.BmpEncoder());
        ms.Position = 0;
        return ms;
    }

    private static Stream CreateWebpStream(int width = 100, int height = 100)
    {
        using var image = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(width, height);
        var ms = new MemoryStream();
        image.Save(ms, new SixLabors.ImageSharp.Formats.Webp.WebpEncoder());
        ms.Position = 0;
        return ms;
    }

    private static Stream CreateGifStream(int width = 100, int height = 100)
    {
        using var image = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(width, height);
        var ms = new MemoryStream();
        image.Save(ms, new SixLabors.ImageSharp.Formats.Gif.GifEncoder());
        ms.Position = 0;
        return ms;
    }

    private static readonly byte[] GifHeader = "GIF89a"u8.ToArray();

    private static Stream CreateGifHeaderStream()
    {
        var ms = new MemoryStream();
        ms.Write(GifHeader, 0, GifHeader.Length);
        ms.Write(new byte[100]);
        ms.Position = 0;
        return ms;
    }

    private void SetupBucketForType(UploadFileType type, string bucketName)
    {
        _bucketRegistry.Setup(r => r.GetBucketName(type)).Returns(bucketName);
    }

    private void SetupUploadReturnsEtag(string bucket, string etag = "test-etag")
    {
        _s3Uploader.Setup(u => u.UploadAsync(bucket, It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>()))
            .ReturnsAsync(etag);
    }

    #region Error cases

    [Fact]
    public async Task Handle_FileNotFound_Throws()
    {
        using var command = new UploadFileCommand
        {
            FileId = Guid.NewGuid(),
            FileStream = new MemoryStream(),
            FileName = "test.jpg",
            FileSize = 0
        };

        var act = () => _handler.Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<Exception>().WithMessage("File not found");
    }

    [Fact]
    public async Task Handle_FileAlreadyUploaded_Throws()
    {
        var file = await _helper.SeedFile(etag: "already-uploaded");

        using var command = new UploadFileCommand
        {
            FileId = file.Id,
            FileStream = new MemoryStream([1, 2, 3]),
            FileName = "test.jpg",
            FileSize = 3
        };

        var act = () => _handler.Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<FileAlreadyUploadedException>();
    }

    #endregion

    #region Basic document upload

    [Fact]
    public async Task Handle_DocumentUploadsToS3()
    {
        var file = await _helper.SeedFile(type: UploadFileType.MessageAttachmentDocument);
        var data = new byte[] { 1, 2, 3, 4, 5 };

        SetupBucketForType(UploadFileType.MessageAttachmentDocument, "message-documents");
        SetupUploadReturnsEtag("message-documents");

        using var command = new UploadFileCommand
        {
            FileId = file.Id,
            FileStream = new MemoryStream(data),
            FileName = "doc.pdf",
            FileSize = data.Length
        };

        var result = await _handler.Handle(command, CancellationToken.None);

        result.Should().Be(file.Id.ToString());
        _s3Uploader.Verify(
            u => u.UploadAsync("message-documents", $"{file.Id}", It.IsAny<Stream>(), "application/pdf"),
            Times.Once());
    }

    [Fact]
    public async Task Handle_Document_NoTypeDetection()
    {
        var file = await _helper.SeedFile(type: UploadFileType.MessageAttachmentDocument);

        SetupBucketForType(UploadFileType.MessageAttachmentDocument, "message-documents");
        SetupUploadReturnsEtag("message-documents");

        using var command = new UploadFileCommand
        {
            FileId = file.Id,
            FileStream = new MemoryStream([1, 2, 3]),
            FileName = "doc.pdf",
            FileSize = 3
        };

        await _handler.Handle(command, CancellationToken.None);

        _bucketRegistry.Verify(r => r.GetBucketName(UploadFileType.MessageAttachmentDocument), Times.Once());
    }

    #endregion

    #region FileSize fallback

    [Fact]
    public async Task Handle_FileSizeZero_UsesStreamLength()
    {
        var data = new byte[] { 10, 20, 30, 40, 50 };
        var file = await _helper.SeedFile(type: UploadFileType.MessageAttachmentDocument);

        SetupBucketForType(UploadFileType.MessageAttachmentDocument, "message-documents");
        SetupUploadReturnsEtag("message-documents");

        using var command = new UploadFileCommand
        {
            FileId = file.Id,
            FileStream = new MemoryStream(data),
            FileName = "file.bin",
            FileSize = 0
        };

        await _handler.Handle(command, CancellationToken.None);

        var updated = await _helper.UploadedFilesStorage.GetFile(file.Id);
        updated!.Size.Should().Be(data.Length);
    }

    [Fact]
    public async Task Handle_FileSizeProvided_UsesProvidedSize()
    {
        var file = await _helper.SeedFile(type: UploadFileType.MessageAttachmentDocument);

        SetupBucketForType(UploadFileType.MessageAttachmentDocument, "message-documents");
        SetupUploadReturnsEtag("message-documents");

        using var command = new UploadFileCommand
        {
            FileId = file.Id,
            FileStream = new MemoryStream([1, 2, 3]),
            FileName = "file.bin",
            FileSize = 999
        };

        await _handler.Handle(command, CancellationToken.None);

        var updated = await _helper.UploadedFilesStorage.GetFile(file.Id);
        updated!.Size.Should().Be(999);
    }

    #endregion

    #region Type detection

    [Fact]
    public async Task Handle_TypeDetection_ImageStaysImage()
    {
        var file = await _helper.SeedFile(type: UploadFileType.MessageAttachmentImage);

        SetupBucketForType(UploadFileType.MessageAttachmentImage, "message-images");
        SetupUploadReturnsEtag("message-images");

        using var command = new UploadFileCommand
        {
            FileId = file.Id,
            FileStream = CreateJpegStream(),
            FileName = "photo.jpg",
            FileSize = 0
        };

        var result = await _handler.Handle(command, CancellationToken.None);

        result.Should().Be(file.Id.ToString());
        _bucketRegistry.Verify(r => r.GetBucketName(UploadFileType.MessageAttachmentImage), Times.Once());
    }

    [Fact]
    public async Task Handle_TypeDetection_ImageChangedToGif()
    {
        var file = await _helper.SeedFile(type: UploadFileType.MessageAttachmentImage);

        SetupBucketForType(UploadFileType.MessageAttachmentImage, "message-images");
        SetupBucketForType(UploadFileType.MessageAttachmentGif, "message-videos");
        SetupUploadReturnsEtag("message-videos");

        using var command = new UploadFileCommand
        {
            FileId = file.Id,
            FileStream = CreateGifHeaderStream(),
            FileName = "animation.gif",
            FileSize = 106
        };

        var result = await _handler.Handle(command, CancellationToken.None);

        result.Should().Be(file.Id.ToString());
        _bucketRegistry.Verify(r => r.GetBucketName(UploadFileType.MessageAttachmentGif), Times.Once());
    }

    [Fact]
    public async Task Handle_TypeDetection_UnknownDetected_KeepsOriginalType()
    {
        var file = await _helper.SeedFile(type: UploadFileType.MessageAttachmentImage);

        SetupBucketForType(UploadFileType.MessageAttachmentImage, "message-images");
        SetupUploadReturnsEtag("message-images");

        using var command = new UploadFileCommand
        {
            FileId = file.Id,
            FileStream = CreateBmpStream(),
            FileName = "image.bmp",
            FileSize = 0
        };

        var result = await _handler.Handle(command, CancellationToken.None);

        result.Should().Be(file.Id.ToString());
        _bucketRegistry.Verify(r => r.GetBucketName(UploadFileType.MessageAttachmentImage), Times.Once());
    }

    #endregion

    #region Sticker validation

    [Fact]
    public async Task Handle_StickerTooLarge_Throws()
    {
        var file = await _helper.SeedFile(type: UploadFileType.MessageAttachmentSticker);

        using var command = new UploadFileCommand
        {
            FileId = file.Id,
            FileStream = new MemoryStream([1, 2, 3]),
            FileName = "sticker.webp",
            FileSize = 13 * 1024 * 1024
        };

        var act = () => _handler.Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<StickerTooLargeException>();
    }

    [Fact]
    public async Task Handle_StickerDimensionsExceeded_Throws()
    {
        var file = await _helper.SeedFile(type: UploadFileType.MessageAttachmentSticker);

        using var command = new UploadFileCommand
        {
            FileId = file.Id,
            FileStream = CreateWebpStream(1200, 1200),
            FileName = "sticker.webp",
            FileSize = 0
        };

        var act = () => _handler.Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<StickerDimensionExceededException>();
    }

    [Fact]
    public async Task Handle_StickerValidSizeAndDimensions_Uploads()
    {
        var file = await _helper.SeedFile(type: UploadFileType.MessageAttachmentSticker);

        SetupBucketForType(UploadFileType.MessageAttachmentSticker, "message-documents");
        SetupUploadReturnsEtag("message-documents");

        using var command = new UploadFileCommand
        {
            FileId = file.Id,
            FileStream = CreateWebpStream(512, 512),
            FileName = "sticker.webp",
            FileSize = 0
        };

        var result = await _handler.Handle(command, CancellationToken.None);

        result.Should().Be(file.Id.ToString());
        _s3Uploader.Verify(
            u => u.UploadAsync("message-documents", $"{file.Id}", It.IsAny<Stream>(), It.IsAny<string>()),
            Times.Once());
    }

    #endregion

    #region Image processing - dimensions

    [Fact]
    public async Task Handle_ImageUpload_SetsDimensions()
    {
        var file = await _helper.SeedFile(type: UploadFileType.MessageAttachmentImage);

        SetupBucketForType(UploadFileType.MessageAttachmentImage, "message-images");
        SetupUploadReturnsEtag("message-images");

        using var command = new UploadFileCommand
        {
            FileId = file.Id,
            FileStream = CreateJpegStream(200, 150),
            FileName = "photo.jpg",
            FileSize = 0
        };

        await _handler.Handle(command, CancellationToken.None);

        var updated = await _helper.UploadedFilesStorage.GetFile(file.Id);
        updated!.ImageWidth.Should().Be(200);
        updated.ImageHeight.Should().Be(150);
    }

    [Fact]
    public async Task Handle_ChatPicture_SetsDimensions()
    {
        var file = await _helper.SeedFile(type: UploadFileType.ChatPicture);

        SetupBucketForType(UploadFileType.ChatPicture, "chat-pictures");
        SetupUploadReturnsEtag("chat-pictures");

        using var command = new UploadFileCommand
        {
            FileId = file.Id,
            FileStream = CreateJpegStream(300, 200),
            FileName = "chat.jpg",
            FileSize = 0
        };

        await _handler.Handle(command, CancellationToken.None);

        var updated = await _helper.UploadedFilesStorage.GetFile(file.Id);
        updated!.ImageWidth.Should().Be(300);
        updated.ImageHeight.Should().Be(200);
    }

    [Fact]
    public async Task Handle_UserAvatar_SetsDimensions()
    {
        var file = await _helper.SeedFile(type: UploadFileType.UserAvatar);

        SetupBucketForType(UploadFileType.UserAvatar, "profile-pictures");
        SetupUploadReturnsEtag("profile-pictures");

        using var command = new UploadFileCommand
        {
            FileId = file.Id,
            FileStream = CreateJpegStream(400, 400),
            FileName = "avatar.jpg",
            FileSize = 0
        };

        await _handler.Handle(command, CancellationToken.None);

        var updated = await _helper.UploadedFilesStorage.GetFile(file.Id);
        updated!.ImageWidth.Should().Be(400);
        updated.ImageHeight.Should().Be(400);
    }

    [Fact]
    public async Task Handle_NonImageContent_NoDimensionsSet()
    {
        var file = await _helper.SeedFile(type: UploadFileType.MessageAttachmentDocument);

        SetupBucketForType(UploadFileType.MessageAttachmentDocument, "message-documents");
        SetupUploadReturnsEtag("message-documents");

        using var command = new UploadFileCommand
        {
            FileId = file.Id,
            FileStream = new MemoryStream([1, 2, 3]),
            FileName = "doc.pdf",
            FileSize = 3
        };

        await _handler.Handle(command, CancellationToken.None);

        var updated = await _helper.UploadedFilesStorage.GetFile(file.Id);
        updated!.ImageWidth.Should().BeNull();
        updated.ImageHeight.Should().BeNull();
    }

    #endregion

    #region Image processing - preview

    [Fact]
    public async Task Handle_ChatPicture_GeneratesPreview()
    {
        var file = await _helper.SeedFile(type: UploadFileType.ChatPicture);

        SetupBucketForType(UploadFileType.ChatPicture, "chat-pictures");
        SetupUploadReturnsEtag("chat-pictures");

        using var command = new UploadFileCommand
        {
            FileId = file.Id,
            FileStream = CreateJpegStream(),
            FileName = "chat.jpg",
            FileSize = 0
        };

        await _handler.Handle(command, CancellationToken.None);

        var updated = await _helper.UploadedFilesStorage.GetFile(file.Id);
        updated!.PreviewId.Should().NotBeNull();

        _s3Uploader.Verify(
            u => u.UploadAsync("chat-pictures", It.IsAny<string>(), It.IsAny<Stream>(), "image/jpeg"),
            Times.Exactly(2));
    }

    [Fact]
    public async Task Handle_UserAvatar_GeneratesPreview()
    {
        var file = await _helper.SeedFile(type: UploadFileType.UserAvatar);

        SetupBucketForType(UploadFileType.UserAvatar, "profile-pictures");
        SetupUploadReturnsEtag("profile-pictures");

        using var command = new UploadFileCommand
        {
            FileId = file.Id,
            FileStream = CreateJpegStream(),
            FileName = "avatar.jpg",
            FileSize = 0
        };

        await _handler.Handle(command, CancellationToken.None);

        var updated = await _helper.UploadedFilesStorage.GetFile(file.Id);
        updated!.PreviewId.Should().NotBeNull();

        _s3Uploader.Verify(
            u => u.UploadAsync("profile-pictures", It.IsAny<string>(), It.IsAny<Stream>(), "image/jpeg"),
            Times.Exactly(2));
    }

    [Fact]
    public async Task Handle_Document_NoPreviewGenerated()
    {
        var file = await _helper.SeedFile(type: UploadFileType.MessageAttachmentDocument);

        SetupBucketForType(UploadFileType.MessageAttachmentDocument, "message-documents");
        SetupUploadReturnsEtag("message-documents");

        using var command = new UploadFileCommand
        {
            FileId = file.Id,
            FileStream = new MemoryStream([1, 2, 3]),
            FileName = "doc.pdf",
            FileSize = 3
        };

        await _handler.Handle(command, CancellationToken.None);

        var updated = await _helper.UploadedFilesStorage.GetFile(file.Id);
        updated!.PreviewId.Should().BeNull();

        _s3Uploader.Verify(
            u => u.UploadAsync("message-documents", It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>()),
            Times.Once());
    }

    [Fact]
    public async Task Handle_PreviewUploadFails_FileStillSavedWithoutPreview()
    {
        var file = await _helper.SeedFile(type: UploadFileType.ChatPicture);
        var callCount = 0;

        SetupBucketForType(UploadFileType.ChatPicture, "chat-pictures");
        _s3Uploader.Setup(u => u.UploadAsync("chat-pictures", It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                if (callCount == 1) return "etag-main";
                throw new Exception("S3 preview upload failed");
            });

        using var command = new UploadFileCommand
        {
            FileId = file.Id,
            FileStream = CreateJpegStream(),
            FileName = "chat.jpg",
            FileSize = 0
        };

        var result = await _handler.Handle(command, CancellationToken.None);

        result.Should().Be(file.Id.ToString());

        var updated = await _helper.UploadedFilesStorage.GetFile(file.Id);
        updated!.PreviewId.Should().BeNull();
        updated.Etag.Should().Be("etag-main");
    }

    #endregion

    #region Image processing - compression (enforce limits)

    [Fact]
    public async Task Handle_LargeImage_GetsCompressed()
    {
        var file = await _helper.SeedFile(type: UploadFileType.MessageAttachmentImage);

        SetupBucketForType(UploadFileType.MessageAttachmentImage, "message-images");
        SetupUploadReturnsEtag("message-images");

        using var command = new UploadFileCommand
        {
            FileId = file.Id,
            FileStream = CreateJpegStream(3000, 3000),
            FileName = "large.jpg",
            FileSize = 0
        };

        await _handler.Handle(command, CancellationToken.None);

        var updated = await _helper.UploadedFilesStorage.GetFile(file.Id);
        updated!.ImageWidth.Should().BeLessThanOrEqualTo(2500);
        updated.ImageHeight.Should().BeLessThanOrEqualTo(2500);
    }

    #endregion

    #region Deduplication

    [Fact]
    public async Task Handle_Deduplication_SameType_ReturnsExistingFileId()
    {
        var data = new byte[] { 42, 42, 42 };
        string actualHash;
        using (var sha256 = System.Security.Cryptography.SHA256.Create())
        {
            var hashBytes = sha256.ComputeHash(data);
            actualHash = Convert.ToHexString(hashBytes).ToLowerInvariant();
        }

        var existingFile = await _helper.SeedFile(type: UploadFileType.MessageAttachmentDocument, etag: "existing-etag");
        var newFile = await _helper.SeedFile(type: UploadFileType.MessageAttachmentDocument);

        await _helper.SeedFileHash(existingFile.Id, actualHash);

        SetupBucketForType(UploadFileType.MessageAttachmentDocument, "message-documents");

        using var command = new UploadFileCommand
        {
            FileId = newFile.Id,
            FileStream = new MemoryStream(data),
            FileName = "dup.txt",
            FileSize = data.Length
        };

        var result = await _handler.Handle(command, CancellationToken.None);

        result.Should().Be(existingFile.Id.ToString());
        _s3Uploader.Verify(
            u => u.UploadAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>()),
            Times.Never());
    }

    [Fact]
    public async Task Handle_Deduplication_DifferentType_UploadsAnyway()
    {
        var data = new byte[] { 42, 42, 42 };
        string actualHash;
        using (var sha256 = System.Security.Cryptography.SHA256.Create())
        {
            var hashBytes = sha256.ComputeHash(data);
            actualHash = Convert.ToHexString(hashBytes).ToLowerInvariant();
        }

        var existingFile = await _helper.SeedFile(type: UploadFileType.MessageAttachmentDocument, etag: "existing-etag");
        var newFile = await _helper.SeedFile(type: UploadFileType.MessageAttachmentImage);

        await _helper.SeedFileHash(existingFile.Id, actualHash);

        SetupBucketForType(UploadFileType.MessageAttachmentImage, "message-images");
        SetupUploadReturnsEtag("message-images");

        using var jpegStream = CreateJpegStream();
        using var command = new UploadFileCommand
        {
            FileId = newFile.Id,
            FileStream = jpegStream,
            FileName = "photo.jpg",
            FileSize = 0
        };

        var result = await _handler.Handle(command, CancellationToken.None);

        result.Should().Be(newFile.Id.ToString());
        _s3Uploader.Verify(
            u => u.UploadAsync("message-images", $"{newFile.Id}", It.IsAny<Stream>(), It.IsAny<string>()),
            Times.Once());
    }

    [Fact]
    public async Task Handle_Deduplication_ExistingFileNoEtag_UploadsAnyway()
    {
        var data = new byte[] { 42, 42, 42 };
        string actualHash;
        using (var sha256 = System.Security.Cryptography.SHA256.Create())
        {
            var hashBytes = sha256.ComputeHash(data);
            actualHash = Convert.ToHexString(hashBytes).ToLowerInvariant();
        }

        var existingFile = await _helper.SeedFile(type: UploadFileType.MessageAttachmentDocument, etag: null);
        var newFile = await _helper.SeedFile(type: UploadFileType.MessageAttachmentDocument);

        await _helper.SeedFileHash(existingFile.Id, actualHash);

        SetupBucketForType(UploadFileType.MessageAttachmentDocument, "message-documents");
        SetupUploadReturnsEtag("message-documents");

        using var command = new UploadFileCommand
        {
            FileId = newFile.Id,
            FileStream = new MemoryStream(data),
            FileName = "doc.txt",
            FileSize = data.Length
        };

        var result = await _handler.Handle(command, CancellationToken.None);

        result.Should().Be(newFile.Id.ToString());
        _s3Uploader.Verify(
            u => u.UploadAsync("message-documents", It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>()),
            Times.Once());
    }

    [Fact]
    public async Task Handle_NoHashFound_UploadsNormally()
    {
        var file = await _helper.SeedFile(type: UploadFileType.MessageAttachmentDocument);

        SetupBucketForType(UploadFileType.MessageAttachmentDocument, "message-documents");
        SetupUploadReturnsEtag("message-documents");

        using var command = new UploadFileCommand
        {
            FileId = file.Id,
            FileStream = new MemoryStream([1, 2, 3]),
            FileName = "doc.pdf",
            FileSize = 3
        };

        var result = await _handler.Handle(command, CancellationToken.None);

        result.Should().Be(file.Id.ToString());
    }

    #endregion

    #region Metadata persistence

    [Fact]
    public async Task Handle_UpdatesFileMetadata()
    {
        var file = await _helper.SeedFile(type: UploadFileType.MessageAttachmentDocument);
        var data = new byte[] { 1, 2, 3 };

        SetupBucketForType(UploadFileType.MessageAttachmentDocument, "message-documents");
        SetupUploadReturnsEtag("message-documents", "etag-abc");

        using var command = new UploadFileCommand
        {
            FileId = file.Id,
            FileStream = new MemoryStream(data),
            FileName = "document.pdf",
            FileSize = data.Length
        };

        await _handler.Handle(command, CancellationToken.None);

        var updated = await _helper.UploadedFilesStorage.GetFile(file.Id);
        updated!.Etag.Should().Be("etag-abc");
        updated.UploadedAt.Should().NotBeNull();
        updated.Filename.Should().Be("document.pdf");
        updated.Size.Should().Be(3);
    }

    [Fact]
    public async Task Handle_SavesFileHash()
    {
        var file = await _helper.SeedFile(type: UploadFileType.MessageAttachmentDocument);
        var data = new byte[] { 10, 20, 30 };

        SetupBucketForType(UploadFileType.MessageAttachmentDocument, "message-documents");
        SetupUploadReturnsEtag("message-documents");

        using var command = new UploadFileCommand
        {
            FileId = file.Id,
            FileStream = new MemoryStream(data),
            FileName = "file.txt",
            FileSize = data.Length
        };

        await _handler.Handle(command, CancellationToken.None);

        var hash = _helper.DbContext.FileHashes.SingleOrDefault(h => h.FileId == file.Id);
        hash.Should().NotBeNull();
        hash!.Hash.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Handle_HashIsActualSha256()
    {
        var data = new byte[] { 55, 66, 77 };
        string expectedHash;
        using (var sha256 = System.Security.Cryptography.SHA256.Create())
        {
            var hashBytes = sha256.ComputeHash(data);
            expectedHash = Convert.ToHexString(hashBytes).ToLowerInvariant();
        }

        var file = await _helper.SeedFile(type: UploadFileType.MessageAttachmentDocument);

        SetupBucketForType(UploadFileType.MessageAttachmentDocument, "message-documents");
        SetupUploadReturnsEtag("message-documents");

        using var command = new UploadFileCommand
        {
            FileId = file.Id,
            FileStream = new MemoryStream(data),
            FileName = "file.bin",
            FileSize = data.Length
        };

        await _handler.Handle(command, CancellationToken.None);

        var hash = _helper.DbContext.FileHashes.Single(h => h.FileId == file.Id);
        hash.Hash.Should().Be(expectedHash);
    }

    #endregion

    #region Content type resolution

    [Theory]
    [InlineData("photo.jpg", "image/jpeg")]
    [InlineData("photo.png", "image/png")]
    [InlineData("doc.pdf", "application/pdf")]
    [InlineData("video.mp4", "video/mp4")]
    [InlineData("audio.mp3", "audio/mpeg")]
    [InlineData("file.webp", "image/webp")]
    public async Task Handle_ContentTypeResolvedFromExtension(string filename, string expectedContentType)
    {
        var file = await _helper.SeedFile(type: UploadFileType.MessageAttachmentDocument);

        SetupBucketForType(UploadFileType.MessageAttachmentDocument, "message-documents");
        SetupUploadReturnsEtag("message-documents");

        using var command = new UploadFileCommand
        {
            FileId = file.Id,
            FileStream = new MemoryStream([1, 2, 3]),
            FileName = filename,
            FileSize = 3
        };

        await _handler.Handle(command, CancellationToken.None);

        _s3Uploader.Verify(
            u => u.UploadAsync("message-documents", $"{file.Id}", It.IsAny<Stream>(), expectedContentType),
            Times.Once());
    }

    #endregion

    #region Various file types upload

    [Fact]
    public async Task Handle_ChatPictureUpload_Succeeds()
    {
        var file = await _helper.SeedFile(type: UploadFileType.ChatPicture);

        SetupBucketForType(UploadFileType.ChatPicture, "chat-pictures");
        SetupUploadReturnsEtag("chat-pictures");

        using var command = new UploadFileCommand
        {
            FileId = file.Id,
            FileStream = CreateJpegStream(),
            FileName = "chat.jpg",
            FileSize = 0
        };

        var result = await _handler.Handle(command, CancellationToken.None);

        result.Should().Be(file.Id.ToString());
    }

    [Fact]
    public async Task Handle_UserAvatarUpload_Succeeds()
    {
        var file = await _helper.SeedFile(type: UploadFileType.UserAvatar);

        SetupBucketForType(UploadFileType.UserAvatar, "profile-pictures");
        SetupUploadReturnsEtag("profile-pictures");

        using var command = new UploadFileCommand
        {
            FileId = file.Id,
            FileStream = CreateJpegStream(),
            FileName = "avatar.jpg",
            FileSize = 0
        };

        var result = await _handler.Handle(command, CancellationToken.None);

        result.Should().Be(file.Id.ToString());
    }

    [Fact]
    public async Task Handle_UserProfilePosterUpload_Succeeds()
    {
        var file = await _helper.SeedFile(type: UploadFileType.UserProfilePoster);

        SetupBucketForType(UploadFileType.UserProfilePoster, "profile-pictures");
        SetupUploadReturnsEtag("profile-pictures");

        using var command = new UploadFileCommand
        {
            FileId = file.Id,
            FileStream = CreateJpegStream(),
            FileName = "poster.jpg",
            FileSize = 0
        };

        var result = await _handler.Handle(command, CancellationToken.None);

        result.Should().Be(file.Id.ToString());

        var updated = await _helper.UploadedFilesStorage.GetFile(file.Id);
        updated!.ImageWidth.Should().NotBeNull();
        updated.PreviewId.Should().BeNull();
    }

    [Fact]
    public async Task Handle_VideoUpload_Succeeds()
    {
        var file = await _helper.SeedFile(type: UploadFileType.MessageAttachmentVideo);

        SetupBucketForType(UploadFileType.MessageAttachmentVideo, "message-videos");
        SetupUploadReturnsEtag("message-videos");

        using var command = new UploadFileCommand
        {
            FileId = file.Id,
            FileStream = new MemoryStream([1, 2, 3]),
            FileName = "video.mp4",
            FileSize = 3
        };

        var result = await _handler.Handle(command, CancellationToken.None);

        result.Should().Be(file.Id.ToString());

        var updated = await _helper.UploadedFilesStorage.GetFile(file.Id);
        updated!.ImageWidth.Should().BeNull();
        updated.PreviewId.Should().BeNull();
    }

    [Fact]
    public async Task Handle_AudioUpload_Succeeds()
    {
        var file = await _helper.SeedFile(type: UploadFileType.MessageAttachmentAudio);

        SetupBucketForType(UploadFileType.MessageAttachmentAudio, "message-audio");
        SetupUploadReturnsEtag("message-audio");

        using var command = new UploadFileCommand
        {
            FileId = file.Id,
            FileStream = new MemoryStream([1, 2, 3]),
            FileName = "audio.mp3",
            FileSize = 3
        };

        var result = await _handler.Handle(command, CancellationToken.None);

        result.Should().Be(file.Id.ToString());
    }

    [Fact]
    public async Task Handle_VoiceUpload_Succeeds()
    {
        var file = await _helper.SeedFile(type: UploadFileType.MessageAttachmentVoice);

        SetupBucketForType(UploadFileType.MessageAttachmentVoice, "message-audio");
        SetupUploadReturnsEtag("message-audio");

        using var command = new UploadFileCommand
        {
            FileId = file.Id,
            FileStream = new MemoryStream([1, 2, 3]),
            FileName = "voice.ogg",
            FileSize = 3
        };

        var result = await _handler.Handle(command, CancellationToken.None);

        result.Should().Be(file.Id.ToString());
    }

    [Fact]
    public async Task Handle_GifUpload_SetsDimensionsNoPreview()
    {
        var file = await _helper.SeedFile(type: UploadFileType.MessageAttachmentGif);

        SetupBucketForType(UploadFileType.MessageAttachmentGif, "message-videos");
        SetupUploadReturnsEtag("message-videos");

        using var command = new UploadFileCommand
        {
            FileId = file.Id,
            FileStream = CreateGifStream(200, 100),
            FileName = "anim.gif",
            FileSize = 0
        };

        var result = await _handler.Handle(command, CancellationToken.None);

        result.Should().Be(file.Id.ToString());

        var updated = await _helper.UploadedFilesStorage.GetFile(file.Id);
        updated!.ImageWidth.Should().Be(200);
        updated.ImageHeight.Should().Be(100);
        updated.PreviewId.Should().BeNull();
    }

    #endregion
}
