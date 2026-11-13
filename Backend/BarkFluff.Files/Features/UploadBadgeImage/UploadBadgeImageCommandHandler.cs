using BarkFluff.Files.Domain;
using BarkFluff.Files.Extensions;
using BarkFluff.Files.Helpers;
using BarkFluff.Files.Infrastructure;
using BarkFluff.Files.Persistence;
using BarkFluff.GrpcServer.Settings;
using BarkFluff.Proto.Files;

using MediatR;

using Grpc.Core;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;

namespace BarkFluff.Files.Features.UploadBadgeImage;

public class UploadBadgeImageCommandHandler : IRequestHandler<UploadBadgeImageCommand, UploadBadgeImageResponse>
{
    private const int MaxImageSizeBytes = 256 * 1024;

    private readonly BadgeImagesStorage _badgeImagesStorage;
    private readonly IS3Uploader _s3Uploader;
    private readonly IS3BucketRegistry _bucketRegistry;
    private readonly IConfiguration _configuration;
    private readonly RunSettings _runSettings;
    private readonly ILogger<UploadBadgeImageCommandHandler> _logger;

    public UploadBadgeImageCommandHandler(
        BadgeImagesStorage badgeImagesStorage,
        IS3Uploader s3Uploader,
        IS3BucketRegistry bucketRegistry,
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
        ValidateImage(request);

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
            "image/png",
            cancellationToken
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

    private static void ValidateImage(UploadBadgeImageCommand request)
    {
        var expectedSize = request.Kind switch
        {
            BadgeImageKind.Full => 256,
            BadgeImageKind.Mini => 32,
            BadgeImageKind.Unspecified => throw InvalidImage("kind обязателен"),
            _ => throw InvalidImage("kind не поддерживается")
        };

        if (request.ImageData.Length > MaxImageSizeBytes)
        {
            throw InvalidImage("Размер изображения не должен превышать 256 КБ");
        }

        try
        {
            var format = Image.DetectFormat(request.ImageData);
            if (format.DefaultMimeType != "image/png")
            {
                throw InvalidImage("Изображение должно быть в формате PNG");
            }

            var imageInfo = Image.Identify(request.ImageData);
            if (imageInfo.Width != expectedSize || imageInfo.Height != expectedSize)
            {
                throw InvalidImage($"Для этого вида бейджа требуется изображение {expectedSize}×{expectedSize} пикселей");
            }
        }
        catch (ImageFormatException ex)
        {
            throw InvalidImage("Не удалось прочитать PNG-изображение", ex);
        }
        catch (NotSupportedException ex)
        {
            throw InvalidImage("Формат изображения не поддерживается", ex);
        }
    }

    private static RpcException InvalidImage(string message, Exception? innerException = null)
    {
        var status = new Status(StatusCode.InvalidArgument, message);
        return innerException is null
            ? new RpcException(status)
            : new RpcException(status, innerException.Message);
    }
}
