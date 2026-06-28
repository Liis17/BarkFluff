using BarkFluff.Files.Helpers;
using BarkFluff.Files.Infrastructure;
using BarkFluff.Files.Persistence;
using BarkFluff.Files.Services;
using BarkFluff.GrpcServer.Settings;
using BarkFluff.Proto.Files;

using MediatR;

using DomainUploadFile = BarkFluff.Files.Domain.UploadFile;
using UploadFileType = BarkFluff.Files.Domain.UploadFileType;

namespace BarkFluff.Files.Features.UploadStickerImage;

public class UploadStickerImageCommandHandler : IRequestHandler<UploadStickerImageCommand, UploadStickerImageResponse>
{
    private readonly UploadedFilesStorage _uploadedFilesStorage;
    private readonly IS3Uploader _s3Uploader;
    private readonly IS3BucketRegistry _bucketRegistry;
    private readonly ImageCompressor _imageCompressor;
    private readonly IConfiguration _configuration;
    private readonly RunSettings _runSettings;
    private readonly ILogger<UploadStickerImageCommandHandler> _logger;

    public UploadStickerImageCommandHandler(
        UploadedFilesStorage uploadedFilesStorage,
        IS3Uploader s3Uploader,
        IS3BucketRegistry bucketRegistry,
        ImageCompressor imageCompressor,
        IConfiguration configuration,
        RunSettings runSettings,
        ILogger<UploadStickerImageCommandHandler> logger)
    {
        _uploadedFilesStorage = uploadedFilesStorage;
        _s3Uploader = s3Uploader;
        _bucketRegistry = bucketRegistry;
        _imageCompressor = imageCompressor;
        _configuration = configuration;
        _runSettings = runSettings;
        _logger = logger;
    }

    public async Task<UploadStickerImageResponse> Handle(UploadStickerImageCommand request, CancellationToken cancellationToken)
    {
        var fileId = Guid.NewGuid();
        var bucketName = _bucketRegistry.GetBucketName(UploadFileType.MessageAttachmentSticker);
        var baseUrl = FileUrlHelper.GetPublicBaseUrl(_configuration, _runSettings);

        _logger.LogInformation("Загрузка изображения стикера {FileId} ({Filename})", fileId, request.Filename);

        // Обработка на сервере: ресайз 512×512 + WebP —
        // вместо Canvas на клиенте, который теряет ICC-профили и даёт жёлтый тон
        using var rawStream = new MemoryStream(request.ImageData);
        var processedBytes = await _imageCompressor.ProcessStickerAsync(rawStream);

        using var stream = new MemoryStream(processedBytes);
        var etag = await _s3Uploader.UploadAsync(bucketName, $"{fileId}", stream, "image/webp");

        var uploadFile = new DomainUploadFile
        {
            Id = fileId,
            Uploaders = new List<long> { 0 },
            CreatedAt = DateTime.UtcNow,
            UploadedAt = DateTime.UtcNow,
            Etag = etag,
            Type = UploadFileType.MessageAttachmentSticker,
            Filename = $"{fileId}.webp",
            Size = processedBytes.Length
        };

        await _uploadedFilesStorage.AddToStorage(uploadFile);

        var fileUrl = FileUrlHelper.GenerateDownloadUrl(baseUrl, fileId);

        _logger.LogInformation("Изображение стикера {FileId} загружено. URL: {Url}", fileId, fileUrl);

        return new UploadStickerImageResponse
        {
            FileId = fileId.ToString(),
            FileUrl = fileUrl
        };
    }
}
