using BarkFluff.Files.Domain;
using BarkFluff.Files.Helpers;
using BarkFluff.Files.Infrastructure;
using BarkFluff.Files.Persistence;
using BarkFluff.GrpcServer.Settings;
using BarkFluff.Proto.Files;
using MediatR;

namespace BarkFluff.Files.Features.UploadBadgeImage;

public class UploadBadgeImageCommandHandler : IRequestHandler<UploadBadgeImageCommand, UploadBadgeImageResponse>
{
    private readonly BadgeImagesStorage _badgeImagesStorage;
    private readonly S3Uploader _s3Uploader;
    private readonly S3BucketRegistry _bucketRegistry;
    private readonly IConfiguration _configuration;
    private readonly RunSettings _runSettings;
    private readonly ILogger<UploadBadgeImageCommandHandler> _logger;

    public UploadBadgeImageCommandHandler(
        BadgeImagesStorage badgeImagesStorage,
        S3Uploader s3Uploader,
        S3BucketRegistry bucketRegistry,
        IConfiguration configuration,
        RunSettings runSettings,
        ILogger<UploadBadgeImageCommandHandler> logger)
    {
        _badgeImagesStorage = badgeImagesStorage;
        _s3Uploader = s3Uploader;
        _bucketRegistry = bucketRegistry;
        _configuration = configuration;
        _runSettings = runSettings;
        _logger = logger;
    }

    public async Task<UploadBadgeImageResponse> Handle(UploadBadgeImageCommand request, CancellationToken cancellationToken)
    {
        var badgeImage = new BadgeImage
        {
            Id = Guid.NewGuid(),
            Filename = request.Filename,
            Size = request.ImageData.Length,
            CreatedAt = DateTime.UtcNow
        };

        _logger.LogInformation("Загрузка картинки бейджа {BadgeImageId} ({Filename})", badgeImage.Id, badgeImage.Filename);

        var bucketName = _bucketRegistry.GetBadgeImageBucketName();

        using var stream = new MemoryStream(request.ImageData);

        var etag = await _s3Uploader.UploadAsync(
            bucketName,
            $"{badgeImage.Id}",
            stream,
            "image/png"
        );

        badgeImage.Etag = etag;
        badgeImage.UploadedAt = DateTime.UtcNow;

        await _badgeImagesStorage.AddAsync(badgeImage);

        var baseUrl = FileUrlHelper.GetPublicBaseUrl(_configuration, _runSettings);
        var permanentUrl = FileUrlHelper.GenerateDownloadUrl(baseUrl, badgeImage.Id);

        _logger.LogInformation("Картинка бейджа {BadgeImageId} успешно загружена. URL: {Url}", badgeImage.Id, permanentUrl);

        return new UploadBadgeImageResponse
        {
            BadgeImageId = badgeImage.Id.ToString(),
            PermanentUrl = permanentUrl
        };
    }
}
