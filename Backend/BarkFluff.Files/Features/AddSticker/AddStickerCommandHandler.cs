using BarkFluff.Files.Domain;
using BarkFluff.Files.Helpers;
using BarkFluff.Files.Infrastructure;
using BarkFluff.Files.Mapping;
using BarkFluff.Files.Persistence;
using BarkFluff.Files.Services;
using BarkFluff.GrpcServer.Settings;
using BarkFluff.Proto.Files;

using MediatR;

using UploadFileType = BarkFluff.Files.Domain.UploadFileType;

namespace BarkFluff.Files.Features.AddSticker;

public class AddStickerCommandHandler : IRequestHandler<AddStickerCommand, AddStickerResponse>
{
    private readonly StickersStorage _stickersStorage;
    private readonly StickerPacksStorage _stickerPacksStorage;
    private readonly UploadedFilesStorage _uploadedFilesStorage;
    private readonly IS3Uploader _s3Uploader;
    private readonly IS3BucketRegistry _bucketRegistry;
    private readonly ImageCompressor _imageCompressor;
    private readonly IConfiguration _configuration;
    private readonly RunSettings _runSettings;
    private readonly ILogger<AddStickerCommandHandler> _logger;

    public AddStickerCommandHandler(
        StickersStorage stickersStorage,
        StickerPacksStorage stickerPacksStorage,
        UploadedFilesStorage uploadedFilesStorage,
        IS3Uploader s3Uploader,
        IS3BucketRegistry bucketRegistry,
        ImageCompressor imageCompressor,
        IConfiguration configuration,
        RunSettings runSettings,
        ILogger<AddStickerCommandHandler> logger)
    {
        _stickersStorage = stickersStorage;
        _stickerPacksStorage = stickerPacksStorage;
        _uploadedFilesStorage = uploadedFilesStorage;
        _s3Uploader = s3Uploader;
        _bucketRegistry = bucketRegistry;
        _imageCompressor = imageCompressor;
        _configuration = configuration;
        _runSettings = runSettings;
        _logger = logger;
    }

    public async Task<AddStickerResponse> Handle(AddStickerCommand request, CancellationToken cancellationToken)
    {
        // Проверяем существование стикерпака
        var pack = await _stickerPacksStorage.GetByIdWithoutStickersAsync(request.PackId)
            ?? throw new Exception("Стикерпак не найден");

        // Проверяем существование файла и его тип
        var file = await _uploadedFilesStorage.GetFile(request.FileId)
            ?? throw new Exception("Файл не найден");

        if (file.Type != UploadFileType.MessageAttachmentSticker)
            throw new Exception("Файл не является стикером (WebP)");

        if (string.IsNullOrEmpty(file.Etag))
            throw new Exception("Файл ещё не загружен в хранилище");

        // Скачиваем оригинал из S3 для генерации превью
        var bucketName = _bucketRegistry.GetBucketName(file.Type);
        using var originalStream = await _s3Uploader.DownloadAsync(bucketName, $"{file.Id}");

        // Генерируем превью 64x64 WebP
        var previewBytes = await _imageCompressor.GenerateStickerPreviewAsync(originalStream);
        var previewId = Guid.NewGuid();

        using var previewStream = new MemoryStream(previewBytes);
        await _s3Uploader.UploadAsync(bucketName, $"{previewId}", previewStream, "image/webp");

        // Создаём запись UploadFile для превью, чтобы оно было доступно через GetTempDownloadUrl и /download/
        var previewFile = new Domain.UploadFile
        {
            Id = previewId,
            Uploaders = file.Uploaders.ToList(),
            CreatedAt = DateTime.UtcNow,
            UploadedAt = DateTime.UtcNow,
            Etag = "sticker-preview",
            Type = UploadFileType.MessageAttachmentSticker,
            Filename = $"{previewId}.webp",
            Size = previewBytes.Length
        };
        await _uploadedFilesStorage.AddToStorage(previewFile);

        _logger.LogInformation("Превью стикера {PreviewId} создано ({Size} байт)", previewId, previewBytes.Length);

        // Создаём запись стикера
        var sticker = new Sticker
        {
            Id = Guid.NewGuid(),
            StickerPackId = request.PackId,
            FileId = request.FileId,
            PreviewFileId = previewId,
            Emoji = request.Emoji,
            AddedAt = DateTime.UtcNow
        };

        await _stickersStorage.AddAsync(sticker);

        _logger.LogInformation("Стикер {StickerId} добавлен в пак {PackId}", sticker.Id, request.PackId);

        var baseUrl = FileUrlHelper.GetPublicBaseUrl(_configuration, _runSettings);
        return new AddStickerResponse { Sticker = sticker.ToGrpc(baseUrl) };
    }
}
