using BarkFluff.Files.Exceptions;
using BarkFluff.Files.Features.DownloadFile;
using BarkFluff.Files.Infrastructure;

using BarkFluff.Files.Domain;

namespace BarkFluff.Files.Tests.Features.DownloadFile;

public class DownloadFileCommandHandlerTests
{
    private readonly TestHelper _helper = new();
    private readonly Mock<IS3Uploader> _s3Uploader;
    private readonly Mock<IS3BucketRegistry> _bucketRegistry;
    private readonly DownloadFileCommandHandler _handler;

    public DownloadFileCommandHandlerTests()
    {
        _s3Uploader = _helper.S3UploaderMock;
        _bucketRegistry = _helper.S3BucketRegistryMock;
        _handler = new DownloadFileCommandHandler(
            _helper.UploadedFilesStorage,
            _s3Uploader.Object,
            _bucketRegistry.Object,
            _helper.TempFilesStorage,
            _helper.BadgeImagesStorage,
            TestHelper.CreateLogger<DownloadFileCommandHandler>()
        );
    }

    private static Stream CreateStream() => new MemoryStream([1, 2, 3, 4, 5]);

    private DownloadFileCommand Command(Guid fileId) => new() { FileId = fileId };

    [Fact]
    public async Task Handle_DownloadUserAvatar_ReturnsFile()
    {
        var file = await _helper.SeedFile(type: UploadFileType.UserAvatar, etag: "etag-1", filename: "avatar.png");
        _bucketRegistry.Setup(r => r.GetBucketName(UploadFileType.UserAvatar)).Returns("profile-pictures");
        _s3Uploader.Setup(u => u.DownloadAsync("profile-pictures", $"{file.Id}")).ReturnsAsync(CreateStream());

        var result = await _handler.Handle(Command(file.Id), CancellationToken.None);

        result.Should().NotBeNull();
        result.FileStream.Should().NotBeNull();
        result.FileName.Should().Contain(file.Id.ToString());
        result.FileName.Should().EndWith(".png");
        result.ContentType.Should().Be("image/png");
    }

    [Fact]
    public async Task Handle_DownloadChatPicture_ReturnsFile()
    {
        var file = await _helper.SeedFile(type: UploadFileType.ChatPicture, etag: "etag-2", filename: "chat.jpg");
        _bucketRegistry.Setup(r => r.GetBucketName(UploadFileType.ChatPicture)).Returns("chat-pictures");
        _s3Uploader.Setup(u => u.DownloadAsync("chat-pictures", $"{file.Id}")).ReturnsAsync(CreateStream());

        var result = await _handler.Handle(Command(file.Id), CancellationToken.None);

        result.Should().NotBeNull();
        result.ContentType.Should().Be("image/jpeg");
    }

    [Fact]
    public async Task Handle_DownloadUserProfilePoster_ReturnsFile()
    {
        var file = await _helper.SeedFile(type: UploadFileType.UserProfilePoster, etag: "etag-3", filename: "poster.webp");
        _bucketRegistry.Setup(r => r.GetBucketName(UploadFileType.UserProfilePoster)).Returns("profile-pictures");
        _s3Uploader.Setup(u => u.DownloadAsync("profile-pictures", $"{file.Id}")).ReturnsAsync(CreateStream());

        var result = await _handler.Handle(Command(file.Id), CancellationToken.None);

        result.Should().NotBeNull();
    }

    [Theory]
    [InlineData(UploadFileType.MessageAttachmentDocument)]
    [InlineData(UploadFileType.MessageAttachmentImage)]
    [InlineData(UploadFileType.MessageAttachmentVideo)]
    [InlineData(UploadFileType.MessageAttachmentAudio)]
    [InlineData(UploadFileType.MessageAttachmentVoice)]
    [InlineData(UploadFileType.MessageAttachmentGif)]
    [InlineData(UploadFileType.MessageAttachmentSticker)]
    [InlineData(UploadFileType.Unknown)]
    public async Task Handle_DownloadDisallowedType_ThrowsException(UploadFileType type)
    {
        var file = await _helper.SeedFile(type: type, etag: "etag");

        var act = () => _handler.Handle(Command(file.Id), CancellationToken.None);

        await act.Should().ThrowAsync<Exception>().WithMessage("Файл не найден");
    }

    [Fact]
    public async Task Handle_FileNotFoundDirectly_FindsViaTempLink()
    {
        var originalFile = await _helper.SeedFile(type: UploadFileType.UserAvatar, etag: "etag-temp", filename: "orig.jpg");
        var tempFile = await _helper.SeedTempFile(originalFile.Id);

        _bucketRegistry.Setup(r => r.GetBucketName(UploadFileType.UserAvatar)).Returns("profile-pictures");
        _s3Uploader.Setup(u => u.DownloadAsync("profile-pictures", $"{originalFile.Id}")).ReturnsAsync(CreateStream());

        var result = await _handler.Handle(Command(tempFile.Id), CancellationToken.None);

        result.Should().NotBeNull();
        result.FileName.Should().EndWith(".jpg");
    }

    [Fact]
    public async Task Handle_TempLinkPointsToNonExistentOriginal_Throws()
    {
        var fakeOriginalId = Guid.NewGuid();
        await _helper.SeedTempFile(fakeOriginalId);

        var act = () => _handler.Handle(Command(fakeOriginalId), CancellationToken.None);

        await act.Should().ThrowAsync<Exception>().WithMessage("Файл не найден");
    }

    [Fact]
    public async Task Handle_FileNotFoundDirectlyOrTemp_FindsBadgeImage()
    {
        var badge = await _helper.SeedBadgeImage(etag: "badge-etag", filename: "badge.png");
        _bucketRegistry.Setup(r => r.GetBadgeImageBucketName()).Returns("badge-images");
        _s3Uploader.Setup(u => u.DownloadAsync("badge-images", $"{badge.Id}")).ReturnsAsync(CreateStream());

        var result = await _handler.Handle(Command(badge.Id), CancellationToken.None);

        result.Should().NotBeNull();
        result.FileName.Should().Contain(badge.Id.ToString());
        result.ContentType.Should().Be("image/png");
    }

    [Fact]
    public async Task Handle_BadgeImageWithoutEtag_ThrowsFileNotUploaded()
    {
        var badge = await _helper.SeedBadgeImage(etag: null);

        var act = () => _handler.Handle(Command(badge.Id), CancellationToken.None);

        await act.Should().ThrowAsync<FileNotUploadedException>();
    }

    [Fact]
    public async Task Handle_FileNotFoundDirectlyOrTempOrBadge_FindsByPreviewId()
    {
        var previewId = Guid.NewGuid();
        var file = await _helper.SeedFile(type: UploadFileType.UserAvatar, etag: "etag-preview", filename: "preview.jpg", previewId: previewId);

        _bucketRegistry.Setup(r => r.GetBucketName(UploadFileType.UserAvatar)).Returns("profile-pictures");
        _s3Uploader.Setup(u => u.DownloadAsync("profile-pictures", $"{previewId}")).ReturnsAsync(CreateStream());

        var result = await _handler.Handle(Command(previewId), CancellationToken.None);

        result.Should().NotBeNull();
        result.FileName.Should().Contain(previewId.ToString());
    }

    [Fact]
    public async Task Handle_NoMatchInAnyLookup_ThrowsFileNotFound()
    {
        var act = () => _handler.Handle(Command(Guid.NewGuid()), CancellationToken.None);

        await act.Should().ThrowAsync<Exception>().WithMessage("Файл не найден");
    }

    [Fact]
    public async Task Handle_FileFoundButNoEtag_ThrowsFileNotUploaded()
    {
        var file = await _helper.SeedFile(type: UploadFileType.UserAvatar, etag: null);

        var act = () => _handler.Handle(Command(file.Id), CancellationToken.None);

        await act.Should().ThrowAsync<FileNotUploadedException>();
    }

    [Fact]
    public async Task Handle_DocumentFile_ReturnsCorrectContentType()
    {
        var file = await _helper.SeedFile(type: UploadFileType.UserAvatar, etag: "etag-doc", filename: "photo.webp");
        _bucketRegistry.Setup(r => r.GetBucketName(UploadFileType.UserAvatar)).Returns("profile-pictures");
        _s3Uploader.Setup(u => u.DownloadAsync("profile-pictures", $"{file.Id}")).ReturnsAsync(CreateStream());

        var result = await _handler.Handle(Command(file.Id), CancellationToken.None);

        result.ContentType.Should().Be("image/webp");
    }

    [Fact]
    public async Task Handle_BadgeImageWithJpgFilename_ReturnsPngContentType()
    {
        var badge = await _helper.SeedBadgeImage(etag: "badge-etag", filename: "badge.jpg");
        _bucketRegistry.Setup(r => r.GetBadgeImageBucketName()).Returns("badge-images");
        _s3Uploader.Setup(u => u.DownloadAsync("badge-images", $"{badge.Id}")).ReturnsAsync(CreateStream());

        var result = await _handler.Handle(Command(badge.Id), CancellationToken.None);

        result.ContentType.Should().Be("image/png");
    }

    [Fact]
    public async Task Handle_BadgeImageFileName_ContainsBadgeId()
    {
        var badgeId = Guid.NewGuid();
        var badge = await _helper.SeedBadgeImage(id: badgeId, etag: "badge-etag", filename: "custom.png");
        _bucketRegistry.Setup(r => r.GetBadgeImageBucketName()).Returns("badge-images");
        _s3Uploader.Setup(u => u.DownloadAsync("badge-images", $"{badgeId}")).ReturnsAsync(CreateStream());

        var result = await _handler.Handle(Command(badgeId), CancellationToken.None);

        result.FileName.Should().Be($"{badgeId}.png");
    }
}
