using System.Security.Claims;
using BarkFluff.Files.Domain;
using BarkFluff.Files.Infrastructure;
using BarkFluff.Files.Persistence;
using BarkFluff.Files.Services;
using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Shared.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace BarkFluff.Files.Tests;

public class TestHelper
{
    public FilesContext DbContext { get; }
    public UploadedFilesStorage UploadedFilesStorage { get; }
    public TempFilesStorage TempFilesStorage { get; }
    public FileHashesStorage FileHashesStorage { get; }
    public BadgeImagesStorage BadgeImagesStorage { get; }
    public StickerPacksStorage StickerPacksStorage { get; }
    public StickersStorage StickersStorage { get; }
    public Mock<IS3Uploader> S3UploaderMock { get; }
    public Mock<IS3BucketRegistry> S3BucketRegistryMock { get; }
    public ImageCompressor ImageCompressor { get; }
    public FileTypeDetector FileTypeDetector { get; }

    public TestHelper()
    {
        var options = new DbContextOptionsBuilder<FilesContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        DbContext = new FilesContext(options);

        var configMock = new Mock<IConfiguration>();
        configMock.Setup(c => c["TempFiles:ExpiresAt"]).Returns("60");

        UploadedFilesStorage = new UploadedFilesStorage(DbContext);
        TempFilesStorage = new TempFilesStorage(DbContext, configMock.Object);
        FileHashesStorage = new FileHashesStorage(DbContext);
        BadgeImagesStorage = new BadgeImagesStorage(DbContext);
        StickerPacksStorage = new StickerPacksStorage(DbContext);
        StickersStorage = new StickersStorage(DbContext);

        S3UploaderMock = new Mock<IS3Uploader>(MockBehavior.Loose);
        S3BucketRegistryMock = new Mock<IS3BucketRegistry>(MockBehavior.Loose);

        ImageCompressor = new ImageCompressor();
        FileTypeDetector = new FileTypeDetector();
    }

    public UserContext CreateUserContext(long userId, string? deviceId = null)
    {
        var claims = new List<Claim>
        {
            new(IdentityClaims.UserId, userId.ToString()),
            new(IdentityClaims.TokenType, "User"),
        };
        if (deviceId != null)
            claims.Add(new Claim(IdentityClaims.DeviceId, deviceId));

        var identity = new ClaimsIdentity(claims, "TestAuth");
        var principal = new ClaimsPrincipal(identity);

        var httpContext = new DefaultHttpContext { User = principal };
        var httpContextAccessor = new Mock<IHttpContextAccessor>();
        httpContextAccessor.Setup(a => a.HttpContext).Returns(httpContext);

        return new UserContext(httpContextAccessor.Object);
    }

    public static ILogger<T> CreateLogger<T>()
    {
        return Mock.Of<ILogger<T>>();
    }

    public void DetachAll()
    {
        DbContext.ChangeTracker.Clear();
    }

    public async Task<UploadFile> SeedFile(
        Guid? id = null,
        List<long>? uploaders = null,
        UploadFileType type = UploadFileType.MessageAttachmentImage,
        string? etag = null,
        string? filename = "test.jpg",
        long size = 1024,
        Guid? previewId = null,
        int? imageWidth = null,
        int? imageHeight = null)
    {
        var file = new UploadFile
        {
            Id = id ?? Guid.NewGuid(),
            Uploaders = uploaders ?? [1],
            CreatedAt = DateTime.UtcNow,
            UploadedAt = etag is not null ? DateTime.UtcNow : null,
            Etag = etag,
            Type = type,
            Filename = filename,
            Size = size,
            PreviewId = previewId,
            ImageWidth = imageWidth,
            ImageHeight = imageHeight
        };

        DbContext.UploadedFiles.Add(file);
        await DbContext.SaveChangesAsync();
        DetachAll();
        return file;
    }

    public async Task<TempFile> SeedTempFile(Guid originalFileId, DateTime? expiresAt = null)
    {
        var tempFile = new TempFile
        {
            Id = Guid.NewGuid(),
            OriginalFileId = originalFileId,
            ExpiresAt = expiresAt ?? DateTime.UtcNow.AddHours(1)
        };

        DbContext.TempFiles.Add(tempFile);
        await DbContext.SaveChangesAsync();
        DetachAll();
        return tempFile;
    }

    public async Task<FileHash> SeedFileHash(Guid fileId, string hash)
    {
        var fileHash = new FileHash
        {
            FileId = fileId,
            Hash = hash
        };

        DbContext.FileHashes.Add(fileHash);
        await DbContext.SaveChangesAsync();
        DetachAll();
        return fileHash;
    }

    public async Task<BadgeImage> SeedBadgeImage(
        Guid? id = null,
        string filename = "badge.png",
        long size = 512,
        string? etag = "some-etag")
    {
        var badge = new BadgeImage
        {
            Id = id ?? Guid.NewGuid(),
            Filename = filename,
            Size = size,
            CreatedAt = DateTime.UtcNow,
            UploadedAt = etag is not null ? DateTime.UtcNow : null,
            Etag = etag
        };

        DbContext.BadgeImages.Add(badge);
        await DbContext.SaveChangesAsync();
        DetachAll();
        return badge;
    }

    public async Task<StickerPack> SeedStickerPack(
        Guid? id = null,
        long creatorUserId = 1,
        string name = "Test Pack",
        string description = "Test Description",
        Guid? coverStickerId = null)
    {
        var pack = new StickerPack
        {
            Id = id ?? Guid.NewGuid(),
            CreatorUserId = creatorUserId,
            Name = name,
            Description = description,
            CreatedAt = DateTime.UtcNow,
            CoverStickerId = coverStickerId
        };

        DbContext.StickerPacks.Add(pack);
        await DbContext.SaveChangesAsync();
        DetachAll();
        return pack;
    }

    public async Task<Sticker> SeedSticker(
        Guid? id = null,
        Guid stickerPackId = default,
        Guid fileId = default,
        Guid? previewFileId = null,
        string emoji = "😀")
    {
        if (stickerPackId == default)
        {
            var pack = await SeedStickerPack();
            stickerPackId = pack.Id;
        }

        var sticker = new Sticker
        {
            Id = id ?? Guid.NewGuid(),
            StickerPackId = stickerPackId,
            FileId = fileId == default ? Guid.NewGuid() : fileId,
            PreviewFileId = previewFileId,
            Emoji = emoji,
            AddedAt = DateTime.UtcNow
        };

        DbContext.Stickers.Add(sticker);
        await DbContext.SaveChangesAsync();
        DetachAll();
        return sticker;
    }
}
