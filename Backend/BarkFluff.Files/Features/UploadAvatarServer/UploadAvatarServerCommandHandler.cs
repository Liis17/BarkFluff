using BarkFluff.Files.Helpers;
using BarkFluff.Files.Infrastructure;
using BarkFluff.Files.Persistence;
using BarkFluff.Files.Services;
using BarkFluff.GrpcServer.Settings;
using BarkFluff.Proto.Files;

using MediatR;

using DomainUploadFile = BarkFluff.Files.Domain.UploadFile;
using UploadFileType = BarkFluff.Files.Domain.UploadFileType;

namespace BarkFluff.Files.Features.UploadAvatarServer;

public class UploadAvatarServerCommandHandler : IRequestHandler<UploadAvatarServerCommand, UploadAvatarServerResponse>
{
    private readonly UploadedFilesStorage _uploadedFilesStorage;
    private readonly S3Uploader _s3Uploader;
    private readonly S3BucketRegistry _bucketRegistry;
    private readonly ImageCompressor _imageCompressor;
    private readonly IConfiguration _configuration;
    private readonly RunSettings _runSettings;
    private readonly ILogger<UploadAvatarServerCommandHandler> _logger;

    public UploadAvatarServerCommandHandler(
        UploadedFilesStorage uploadedFilesStorage,
        S3Uploader s3Uploader,
        S3BucketRegistry bucketRegistry,
        ImageCompressor imageCompressor,
        IConfiguration configuration,
        RunSettings runSettings,
        ILogger<UploadAvatarServerCommandHandler> logger)
    {
        _uploadedFilesStorage = uploadedFilesStorage;
        _s3Uploader = s3Uploader;
        _bucketRegistry = bucketRegistry;
        _imageCompressor = imageCompressor;
        _configuration = configuration;
        _runSettings = runSettings;
        _logger = logger;
    }

    public async Task<UploadAvatarServerResponse> Handle(UploadAvatarServerCommand request, CancellationToken cancellationToken)
    {
        var bucketName = _bucketRegistry.GetBucketName(UploadFileType.UserAvatar);
        var baseUrl = FileUrlHelper.GetPublicBaseUrl(_configuration, _runSettings);

        // Обработка изображения на сервере (ресайз + JPEG) —
        // вместо Canvas на клиенте, который теряет ICC-профили и даёт жёлтый тон
        var mainFileId = Guid.NewGuid();

        _logger.LogInformation("Загрузка аватара для пользователя {UserId}, fileId={FileId}", request.UserId, mainFileId);

        using var rawStream = new MemoryStream(request.ImageData);
        var processedBytes = await _imageCompressor.ProcessAvatarAsync(rawStream);

        using var mainStream = new MemoryStream(processedBytes);
        var mainEtag = await _s3Uploader.UploadAsync(bucketName, $"{mainFileId}", mainStream, "image/jpeg");

        // Создаём превью (64px)
        var previewFileId = Guid.NewGuid();

        using var previewInputStream = new MemoryStream(processedBytes);
        var previewBytes = await _imageCompressor.CompressImageAsync(previewInputStream, 64);

        using var previewStream = new MemoryStream(previewBytes);
        var previewEtag = await _s3Uploader.UploadAsync(bucketName, $"{previewFileId}", previewStream, "image/jpeg");

        // Сохраняем превью в БД
        var previewFile = new DomainUploadFile
        {
            Id = previewFileId,
            Uploaders = new List<long> { request.UserId },
            CreatedAt = DateTime.UtcNow,
            UploadedAt = DateTime.UtcNow,
            Etag = previewEtag,
            Type = UploadFileType.UserAvatar,
            Filename = $"preview_{mainFileId}.jpg",
            Size = previewBytes.Length
        };
        await _uploadedFilesStorage.AddToStorage(previewFile);

        // Сохраняем основной файл в БД
        var mainFile = new DomainUploadFile
        {
            Id = mainFileId,
            Uploaders = new List<long> { request.UserId },
            CreatedAt = DateTime.UtcNow,
            UploadedAt = DateTime.UtcNow,
            Etag = mainEtag,
            Type = UploadFileType.UserAvatar,
            Filename = $"{mainFileId}.jpg",
            PreviewId = previewFileId,
            Size = processedBytes.Length
        };
        await _uploadedFilesStorage.AddToStorage(mainFile);

        var fileUrl = FileUrlHelper.GenerateDownloadUrl(baseUrl, mainFileId);
        var previewUrl = FileUrlHelper.GenerateDownloadUrl(baseUrl, previewFileId);

        _logger.LogInformation("Аватар для пользователя {UserId} загружен. URL: {FileUrl}, Preview: {PreviewUrl}", request.UserId, fileUrl, previewUrl);

        return new UploadAvatarServerResponse
        {
            FileUrl = fileUrl,
            PreviewUrl = previewUrl,
            FileId = mainFileId.ToString()
        };
    }
}
