using BarkFluff.Files.Domain;
using BarkFluff.Files.Exceptions;
using BarkFluff.Files.Extensions;
using BarkFluff.Files.Infrastructure;
using BarkFluff.Files.Persistence;
using BarkFluff.Files.Services;
using BarkFluff.GrpcServer.Metrics;

using MediatR;

using SixLabors.ImageSharp;

using System.Diagnostics;
using System.Security.Cryptography;

using UploadFileType = BarkFluff.Files.Domain.UploadFileType;

namespace BarkFluff.Files.Features.UploadFile;

public class UploadFileCommandHandler : IRequestHandler<UploadFileCommand, string>
{
    private readonly UploadedFilesStorage _filesStorage;
    private readonly FileHashesStorage _hashesStorage;
    private readonly IS3Uploader _s3Uploader;
    private readonly IS3BucketRegistry _bucketRegistry;
    private readonly ImageCompressor _imageCompressor;
    private readonly FileTypeDetector _fileTypeDetector;
    private readonly VideoThumbnailExtractor _videoThumbnailExtractor;
    private readonly MetricsCollector _metrics;
    private readonly ILogger<UploadFileCommandHandler> _logger;


    private readonly List<UploadFileType> _filesToNeedGeneratePreview
        = [UploadFileType.ChatPicture, UploadFileType.MessageAttachmentImage, UploadFileType.UserAvatar];

    private readonly Dictionary<UploadFileType, int> _customFileTypeWidth = new()
    {
        { UploadFileType.UserAvatar, 64 }
    };

    /// <summary>
    /// Типы файлов, для которых нужно проверять реальный тип по содержимому.
    /// Если пользователь явно выбрал <see cref="UploadFileType.MessageAttachmentDocument"/>,
    /// детекция не выполняется — документ остаётся документом независимо от содержимого.
    /// </summary>
    private static readonly HashSet<UploadFileType> TypesRequiringDetection =
    [
        UploadFileType.MessageAttachmentImage,
        UploadFileType.MessageAttachmentVideo,
        UploadFileType.MessageAttachmentGif,
        UploadFileType.MessageAttachmentAudio,
        UploadFileType.MessageAttachmentVoice,
        UploadFileType.MessageAttachmentSticker
    ];

    /// <summary>
    /// Типы файлов, для которых нужно извлекать размеры изображения.
    /// </summary>
    private static readonly HashSet<UploadFileType> ImageTypesForDimensions =
    [
        UploadFileType.UserAvatar,
        UploadFileType.MessageAttachmentImage,
        UploadFileType.MessageAttachmentGif,
        UploadFileType.ChatPicture,
        UploadFileType.MessageAttachmentSticker,
        UploadFileType.UserProfilePoster
    ];

    public UploadFileCommandHandler(
        UploadedFilesStorage filesStorage,
        FileHashesStorage hashesStorage,
        IS3Uploader s3Uploader,
        IS3BucketRegistry bucketRegistry,
        ImageCompressor imageCompressor,
        FileTypeDetector fileTypeDetector,
        VideoThumbnailExtractor videoThumbnailExtractor,
        MetricsCollector metrics,
        ILogger<UploadFileCommandHandler> logger)
    {
        _filesStorage = filesStorage;
        _hashesStorage = hashesStorage;
        _s3Uploader = s3Uploader;
        _bucketRegistry = bucketRegistry;
        _imageCompressor = imageCompressor;
        _fileTypeDetector = fileTypeDetector;
        _videoThumbnailExtractor = videoThumbnailExtractor;
        _metrics = metrics;
        _logger = logger;
    }

    public async Task<string> Handle(UploadFileCommand request, CancellationToken cancellationToken)
    {
        var totalTimer = Stopwatch.StartNew();
        var bufferingDuration = TimeSpan.Zero;
        var hashDuration = TimeSpan.Zero;
        var processingDuration = TimeSpan.Zero;
        var s3Duration = TimeSpan.Zero;

        _logger.LogInformation("Начало обработки загрузки файла с ID: {FileId}", request.FileId);

        var file = await _filesStorage.GetFile(request.FileId);

        if (file is null)
        {
            _logger.LogError("Файл с ID {FileId} не найден", request.FileId);
            throw new Exception("File not found");
        }

        // Проверяем, не был ли файл уже загружен
        if (!string.IsNullOrEmpty(file.Etag))
        {
            _logger.LogWarning("Файл с ID {FileId} уже был загружен (Etag: {Etag})", request.FileId, file.Etag);
            throw new FileAlreadyUploadedException("Файл уже был загружен");
        }

        file.Filename = request.FileName;

        // Определяем тип контента по расширению файла
        var contentType = request.FileName.GetContentType();

        // Получаем имя бакета в зависимости от типа файла
        var bucketName = _bucketRegistry.GetBucketName(file.Type);

        _logger.LogInformation("Загрузка файла {FileName} с типом {ContentType} в бакет {BucketName}",
            request.FileName, contentType, bucketName);

        long fileSize = request.FileSize > 0 ? request.FileSize : request.FileStream.Length;

        var isImageType = file.Type is UploadFileType.UserAvatar
            or UploadFileType.MessageAttachmentImage
            or UploadFileType.ChatPicture
            or UploadFileType.MessageAttachmentGif;
        var isVideoType = file.Type == UploadFileType.MessageAttachmentVideo;

        Stream originalStream;
        // Путь к временному файлу на диске. Нужен для видео (FFmpeg читает файл по пути),
        // также используется для буферизации больших не-графических файлов. null = буфер в памяти.
        string? tempFilePath = null;

        if (isVideoType || fileSize > 100 * 1024 * 1024)
        {
            // Видео и большие файлы (в т.ч. изображения/GIF) — буферизация через временный файл на диске
            tempFilePath = Path.GetTempFileName();
            _logger.LogInformation("Файл {FileId} ({Size} МБ) буферизуется через диск", request.FileId, fileSize / 1024 / 1024);
            // FileShare.Read — чтобы процесс ffmpeg/ffprobe мог открыть файл параллельно нашему стриму.
            var tempStream = new FileStream(
                tempFilePath, FileMode.Create, FileAccess.ReadWrite,
                FileShare.Read, 81920, FileOptions.None);
            var bufferingTimer = Stopwatch.StartNew();
            await request.FileStream.CopyToAsync(tempStream, cancellationToken);
            bufferingDuration = bufferingTimer.Elapsed;
            tempStream.Position = 0;
            originalStream = tempStream;
        }
        else
        {
            // Изображения и файлы до 100 МБ — в оперативной памяти для скорости
            var memStream = new MemoryStream();
            var bufferingTimer = Stopwatch.StartNew();
            await request.FileStream.CopyToAsync(memStream, cancellationToken);
            bufferingDuration = bufferingTimer.Elapsed;
            memStream.Position = 0;
            originalStream = memStream;
        }

        // Compute SHA256 hash of the file
        // TODO: For better performance with large files, consider computing hash during the initial stream copy
        string fileHash;
        using (var sha256 = SHA256.Create())
        {
            var hashTimer = Stopwatch.StartNew();
            var hashBytes = await sha256.ComputeHashAsync(originalStream, cancellationToken);
            hashDuration = hashTimer.Elapsed;
            fileHash = Convert.ToHexString(hashBytes).ToLowerInvariant();
        }

        originalStream.Position = 0;

        _logger.LogInformation("Вычислен хеш файла: {FileHash}", fileHash);

        var processingTimer = Stopwatch.StartNew();
        // Определяем реальный тип файла по содержимому для MessageAttachment типов
        if (TypesRequiringDetection.Contains(file.Type))
        {
            var detectedType = await _fileTypeDetector.DetectAsync(originalStream);
            var newFileType = MapDetectedTypeToUploadFileType(detectedType, file.Type);

            if (newFileType != file.Type)
            {
                _logger.LogInformation(
                    "Тип файла изменён с {OriginalType} на {NewType} на основе содержимого",
                    file.Type, newFileType);
                file.Type = newFileType;

                // Пересчитываем bucketName для нового типа
                bucketName = _bucketRegistry.GetBucketName(file.Type);

                // Пересчитываем isImageType и contentType после детекции —
                // тип файла мог измениться (например Video → Image)
                isImageType = file.Type is UploadFileType.UserAvatar
                    or UploadFileType.MessageAttachmentImage
                    or UploadFileType.ChatPicture
                    or UploadFileType.MessageAttachmentGif;

                if (isImageType)
                    contentType = request.FileName.GetContentType();
            }
        }

        // Валидация стикеров: макс. 12 МБ, макс. 1024px по любой оси, без сжатия
        if (file.Type == UploadFileType.MessageAttachmentSticker)
        {
            if (fileSize > 12 * 1024 * 1024)
                throw new StickerTooLargeException();

            originalStream.Position = 0;
            var imageInfo = await SixLabors.ImageSharp.Image.IdentifyAsync(originalStream, cancellationToken);
            if (imageInfo is null || imageInfo.Width > 1024 || imageInfo.Height > 1024)
                throw new StickerDimensionExceededException();

            originalStream.Position = 0;
        }

        processingDuration = processingTimer.Elapsed;
        originalStream.Position = 0;

        // Серверная дедупликация: проверяем, существует ли файл с таким хешем
        var existingFileId = await _hashesStorage.GetFileIdByHash(fileHash);
        if (existingFileId.HasValue)
        {
            _logger.LogInformation(
                "Файл с хешем {FileHash} уже существует в хранилище (FileId: {ExistingFileId}). Дедупликация.",
                fileHash, existingFileId.Value);

            var existingFile = await _filesStorage.GetFile(existingFileId.Value);

            // Дедупликация только если тип совпадает и файл реально загружен в S3.
            // Один и тот же контент может быть отправлен как IMAGE и как DOCUMENT —
            // это принципиально разные записи (разные бакеты, разная обработка превью).
            var canDeduplicate = existingFile is not null
                && !string.IsNullOrEmpty(existingFile.Etag)
                && existingFile.Type == file.Type;

            if (canDeduplicate)
            {
                foreach (var uploaderId in file.Uploaders)
                    await _filesStorage.AddUploaderToFile(existingFileId.Value, uploaderId);

                await _filesStorage.DeleteFile(file.Id);
                // Подчищаем возможный висячий хеш для удаляемого file.Id
                // (защита на случай если запись попала в FileHashes от прошлой попытки загрузки)
                await _hashesStorage.DeleteHashByFileId(file.Id, cancellationToken);
                await originalStream.DisposeAsync();

                processingDuration = processingTimer.Elapsed;
                RecordUploadTimings(totalTimer.Elapsed, bufferingDuration, hashDuration, processingDuration, s3Duration);
                return existingFileId.Value.ToString();
            }

            _logger.LogInformation(
                "Дедупликация пропущена для {FileId}: тип отличается ({ExistingType} vs {NewType}) или файл не загружен.",
                file.Id, existingFile?.Type, file.Type);
        }

        try
        {
            var contentProcessingTimer = Stopwatch.StartNew();
            // Объединённая обработка изображения за один Image.LoadAsync:
            // получаем размеры, опционально сжатый оригинал и опционально превью.
            // Пришло на смену трём отдельным декодированиям одного и того же изображения
            // (EnforceOriginalLimitsAsync + Image.IdentifyAsync + CompressImageAsync).
            var isImageContent = contentType.StartsWith("image/");
            var needsDimensions = ImageTypesForDimensions.Contains(file.Type) && isImageContent;
            var needsPreview = _filesToNeedGeneratePreview.Contains(file.Type) && isImageContent;
            var needsEnforceLimits = file.Type == UploadFileType.MessageAttachmentImage && isImageContent;

            Guid? previewId = null;
            byte[]? previewBytes = null;

            if (needsDimensions || needsPreview || needsEnforceLimits)
            {
                originalStream.Position = 0;
                int? previewWidth = needsPreview
                    ? _customFileTypeWidth.GetValueOrDefault(file.Type, 1024)
                    : null;

                ImageProcessingResult? imageResult = null;
                try
                {
                    imageResult = await _imageCompressor.ProcessImageAllInOneAsync(
                        originalStream, needsEnforceLimits, previewWidth, cancellationToken);
                }
                catch (Exception ex)
                {
                    // Файл может быть помечен как изображение, но не быть валидным — продолжаем загрузку оригинала.
                    _logger.LogWarning(ex, "Не удалось обработать изображение {FileId} — обработка пропущена", file.Id);
                }

                if (imageResult is not null)
                {
                    if (needsDimensions)
                    {
                        file.ImageWidth = imageResult.Width;
                        file.ImageHeight = imageResult.Height;
                        _logger.LogInformation(
                            "Размеры изображения {FileId}: {Width}x{Height}",
                            file.Id, imageResult.Width, imageResult.Height);
                    }

                    if (imageResult.CompressedOriginal is not null)
                    {
                        _logger.LogInformation(
                            "Изображение {FileId} сжато сервером (превышены лимиты: 2500px / 2МБ). Новый размер: {NewSize} байт",
                            file.Id, imageResult.CompressedOriginal.Length);

                        await originalStream.DisposeAsync();
                        originalStream = new MemoryStream(imageResult.CompressedOriginal);
                        fileSize = originalStream.Length;
                        contentType = "image/jpeg";
                    }
                    else
                    {
                        originalStream.Position = 0;
                    }

                    if (imageResult.PreviewBytes is not null)
                    {
                        previewId = Guid.NewGuid();
                        previewBytes = imageResult.PreviewBytes;
                    }
                }
                else
                {
                    originalStream.Position = 0;
                }
            }

            // Видео: кадр-обложка на 5-й секунде → тот же pipeline превью, что и для картинок.
            // tempFilePath может быть null, если тип определился как видео только после детекции
            // содержимого (буферизация на диск решается до неё) — в этом случае превью пропускается.
            if (file.Type == UploadFileType.MessageAttachmentVideo && tempFilePath is not null)
            {
                try
                {
                    var frameBytes = await _videoThumbnailExtractor.ExtractFrameJpegAsync(
                        tempFilePath, VideoThumbnailExtractor.DefaultFramePosition, cancellationToken);

                    using var frameStream = new MemoryStream(frameBytes);
                    var previewWidth = _customFileTypeWidth.GetValueOrDefault(file.Type, 1024);
                    var frameResult = await _imageCompressor.ProcessImageAllInOneAsync(
                        frameStream, enforceOriginalLimits: false, previewWidth, cancellationToken);

                    file.ImageWidth = frameResult.Width;
                    file.ImageHeight = frameResult.Height;

                    if (frameResult.PreviewBytes is not null)
                    {
                        previewId = Guid.NewGuid();
                        previewBytes = frameResult.PreviewBytes;
                    }

                    _logger.LogInformation(
                        "Сгенерировано превью видео {FileId}: {Width}x{Height}",
                        file.Id, frameResult.Width, frameResult.Height);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Не удалось сгенерировать превью видео {FileId}", file.Id);
                }
                finally
                {
                    originalStream.Position = 0;
                }
            }

            processingDuration += contentProcessingTimer.Elapsed;

            // Загружаем файл в S3 напрямую из стрима
            var s3Timer = Stopwatch.StartNew();
            var etag = await _s3Uploader.UploadAsync(
                bucketName, // Имя бакета на основе типа файла
                $"{file.Id}", // Используем ID файла как ключ
                originalStream,
                contentType
            );

            _logger.LogInformation("Файл успешно загружен в S3, получен Etag: {Etag}", etag);

            // Загружаем превью (если было сгенерировано)
            if (previewId.HasValue && previewBytes is not null)
            {
                _logger.LogInformation("Создание превью для изображения с ID {FileId}", file.Id);
                try
                {
                    using var compressedStream = new MemoryStream(previewBytes);

                    await _s3Uploader.UploadAsync(
                        bucketName,
                        $"{previewId.Value}", // ключ для сжатой версии
                        compressedStream,
                        "image/jpeg" // сохраняем как JPEG
                    );

                    file.PreviewId = previewId.Value;
                    _logger.LogInformation("Превью успешно создано с ID: {PreviewId}", previewId.Value);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Ошибка при загрузке превью для изображения с ID {FileId}", file.Id);
                }
            }

            s3Duration = s3Timer.Elapsed;

            // Обновляем метаданные файла
            file.Etag = etag;
            file.UploadedAt = DateTime.UtcNow;
            file.Size = fileSize;
        }
        finally
        {
            // Обязательно закрываем поток в конце работы
            await originalStream.DisposeAsync();

            if (tempFilePath is not null)
            {
                try
                {
                    if (File.Exists(tempFilePath))
                        File.Delete(tempFilePath);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Не удалось удалить временный файл {TempPath}", tempFilePath);
                }
            }
        }

        // Сохраняем изменения
        await _filesStorage.UpdateFile(file);

        // Save the file hash for deduplication
        var fileHashEntity = new FileHash
        {
            FileId = file.Id,
            Hash = fileHash
        };
        await _hashesStorage.AddHash(fileHashEntity, cancellationToken);

        RecordUploadTimings(totalTimer.Elapsed, bufferingDuration, hashDuration, processingDuration, s3Duration);

        _logger.LogInformation("Хеш файла сохранен в базу данных");

        _logger.LogInformation("Обработка файла {FileId} успешно завершена", file.Id);

        return file.Id.ToString();
    }

    private void RecordUploadTimings(
        TimeSpan total,
        TimeSpan buffering,
        TimeSpan hashing,
        TimeSpan processing,
        TimeSpan s3)
    {
        _metrics.SetMany(
            new("files_last_upload_total_ms", (long)total.TotalMilliseconds),
            new("files_last_upload_buffering_ms", (long)buffering.TotalMilliseconds),
            new("files_last_upload_hashing_ms", (long)hashing.TotalMilliseconds),
            new("files_last_upload_processing_ms", (long)processing.TotalMilliseconds),
            new("files_last_upload_s3_ms", (long)s3.TotalMilliseconds));
    }

    /// <summary>
    /// Преобразует определённый тип файла в UploadFileType.
    /// Если тип не удалось определить (Unknown), возвращает оригинальный тип пользователя —
    /// это позволяет PNG/JPG, не распознанные детектором, оставаться изображениями, а не
    /// превращаться в документы.
    /// </summary>
    private static UploadFileType MapDetectedTypeToUploadFileType(DetectedFileType detectedType, UploadFileType originalType)
    {
        return detectedType switch
        {
            DetectedFileType.Image => UploadFileType.MessageAttachmentImage,
            DetectedFileType.Video => UploadFileType.MessageAttachmentVideo,
            DetectedFileType.Gif => UploadFileType.MessageAttachmentGif,
            DetectedFileType.Audio => UploadFileType.MessageAttachmentAudio,
            DetectedFileType.Voice => UploadFileType.MessageAttachmentVoice,
            DetectedFileType.Sticker => UploadFileType.MessageAttachmentSticker,
            DetectedFileType.Document => UploadFileType.MessageAttachmentDocument,
            // Unknown означает, что детектор не смог распознать формат —
            // в этом случае доверяем выбору пользователя (originalType).
            _ => originalType
        };
    }
}
