using BarkFluff.Files.Extensions;
using BarkFluff.Files.Helpers;
using BarkFluff.Files.Infrastructure;
using BarkFluff.Files.Persistence;
using BarkFluff.GrpcServer.Settings;
using BarkFluff.Proto.Files;

using MediatR;

using DomainUploadFile = BarkFluff.Files.Domain.UploadFile;
using UploadFileType = BarkFluff.Files.Domain.UploadFileType;

namespace BarkFluff.Files.Features.UploadStickerImage;

public class UploadStickerImageCommandHandler : IRequestHandler<UploadStickerImageCommand, UploadStickerImageResponse>
{
    private readonly UploadedFilesStorage _uploadedFilesStorage;
    private readonly S3Uploader _s3Uploader;
    private readonly S3BucketRegistry _bucketRegistry;
    private readonly IConfiguration _configuration;
    private readonly RunSettings _runSettings;
    private readonly ILogger<UploadStickerImageCommandHandler> _logger;

    public UploadStickerImageCommandHandler(
        UploadedFilesStorage uploadedFilesStorage,
        S3Uploader s3Uploader,
        S3BucketRegistry bucketRegistry,
        IConfiguration configuration,
        RunSettings runSettings,
        ILogger<UploadStickerImageCommandHandler> logger)
    {
        _uploadedFilesStorage = uploadedFilesStorage;
        _s3Uploader = s3Uploader;
        _bucketRegistry = bucketRegistry;
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

        using var stream = new MemoryStream(request.ImageData);
        var contentType = request.Filename.GetContentType();

        var etag = await _s3Uploader.UploadAsync(bucketName, $"{fileId}", stream, contentType);

        var uploadFile = new DomainUploadFile
        {
            Id = fileId,
            Uploaders = new List<long> { 0 },
            CreatedAt = DateTime.UtcNow,
            UploadedAt = DateTime.UtcNow,
            Etag = etag,
            Type = UploadFileType.MessageAttachmentSticker,
            Filename = request.Filename,
            Size = request.ImageData.Length
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
