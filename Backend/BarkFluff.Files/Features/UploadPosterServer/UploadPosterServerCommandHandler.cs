using BarkFluff.Files.Helpers;
using BarkFluff.Files.Infrastructure;
using BarkFluff.Files.Persistence;
using BarkFluff.Files.Services;
using BarkFluff.GrpcServer.Settings;
using BarkFluff.Proto.Files;

using MediatR;

using DomainUploadFile = BarkFluff.Files.Domain.UploadFile;
using UploadFileType = BarkFluff.Files.Domain.UploadFileType;

namespace BarkFluff.Files.Features.UploadPosterServer;

public class UploadPosterServerCommandHandler : IRequestHandler<UploadPosterServerCommand, UploadPosterServerResponse>
{
    private readonly UploadedFilesStorage _uploadedFilesStorage;
    private readonly IS3Uploader _s3Uploader;
    private readonly IS3BucketRegistry _bucketRegistry;
    private readonly ImageCompressor _imageCompressor;
    private readonly IConfiguration _configuration;
    private readonly RunSettings _runSettings;
    private readonly ILogger<UploadPosterServerCommandHandler> _logger;

    public UploadPosterServerCommandHandler(
        UploadedFilesStorage uploadedFilesStorage,
        IS3Uploader s3Uploader,
        IS3BucketRegistry bucketRegistry,
        ImageCompressor imageCompressor,
        IConfiguration configuration,
        RunSettings runSettings,
        ILogger<UploadPosterServerCommandHandler> logger)
    {
        _uploadedFilesStorage = uploadedFilesStorage;
        _s3Uploader = s3Uploader;
        _bucketRegistry = bucketRegistry;
        _imageCompressor = imageCompressor;
        _configuration = configuration;
        _runSettings = runSettings;
        _logger = logger;
    }

    public async Task<UploadPosterServerResponse> Handle(UploadPosterServerCommand request, CancellationToken cancellationToken)
    {
        var bucketName = _bucketRegistry.GetBucketName(UploadFileType.UserProfilePoster);
        var baseUrl = FileUrlHelper.GetPublicBaseUrl(_configuration, _runSettings);

        var fileId = Guid.NewGuid();

        _logger.LogInformation("Загрузка постера профиля для пользователя {UserId}, fileId={FileId}", request.UserId, fileId);

        using var rawStream = new MemoryStream(request.ImageData);
        var processedBytes = await _imageCompressor.ProcessAvatarAsync(rawStream, maxSide: 1920, quality: 85);

        using var fileStream = new MemoryStream(processedBytes);
        var etag = await _s3Uploader.UploadAsync(bucketName, $"{fileId}", fileStream, "image/jpeg");

        var domainFile = new DomainUploadFile
        {
            Id = fileId,
            Uploaders = new List<long> { request.UserId },
            CreatedAt = DateTime.UtcNow,
            UploadedAt = DateTime.UtcNow,
            Etag = etag,
            Type = UploadFileType.UserProfilePoster,
            Filename = $"{fileId}.jpg",
            Size = processedBytes.Length
        };
        await _uploadedFilesStorage.AddToStorage(domainFile);

        var fileUrl = FileUrlHelper.GenerateDownloadUrl(baseUrl, fileId);

        _logger.LogInformation("Постер профиля для пользователя {UserId} загружен. URL: {FileUrl}", request.UserId, fileUrl);

        return new UploadPosterServerResponse
        {
            FileUrl = fileUrl,
            FileId = fileId.ToString()
        };
    }
}
