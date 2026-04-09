using BarkFluff.Files.Domain;
using BarkFluff.Files.Exceptions;
using BarkFluff.Files.Extensions;
using BarkFluff.Files.Infrastructure;
using BarkFluff.Files.Persistence;
using BarkFluff.Files.Services;

using MediatR;

using System.Security.Cryptography;

using UploadFileType = BarkFluff.Files.Domain.UploadFileType;

namespace BarkFluff.Files.Features.UploadFile;

public class UploadFileCommandHandler : IRequestHandler<UploadFileCommand, string>
{
    private readonly UploadedFilesStorage _filesStorage;
    private readonly FileHashesStorage _hashesStorage;
    private readonly S3Uploader _s3Uploader;
    private readonly S3BucketRegistry _bucketRegistry;
    private readonly ImageCompressor _imageCompressor;
    private readonly FileTypeDetector _fileTypeDetector;
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

    public UploadFileCommandHandler(
        UploadedFilesStorage filesStorage,
        FileHashesStorage hashesStorage,
        S3Uploader s3Uploader,
        S3BucketRegistry bucketRegistry,
        ImageCompressor imageCompressor,
        FileTypeDetector fileTypeDetector,
        ILogger<UploadFileCommandHandler> logger)
    {
        _filesStorage = filesStorage;
        _hashesStorage = hashesStorage;
        _s3Uploader = s3Uploader;
        _bucketRegistry = bucketRegistry;
        _imageCompressor = imageCompressor;
        _fileTypeDetector = fileTypeDetector;
        _logger = logger;
    }

    public async Task<string> Handle(UploadFileCommand request, CancellationToken cancellationToken)
    {
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

        Stream originalStream;

        if (!isImageType && fileSize > 100 * 1024 * 1024)
        {
            // Большие не-графические файлы — буферизация через временный файл на диске
            _logger.LogInformation("Файл {FileId} ({Size} МБ) буферизуется через диск", request.FileId, fileSize / 1024 / 1024);
            var tempStream = new FileStream(
                Path.GetTempFileName(), FileMode.Create, FileAccess.ReadWrite,
                FileShare.None, 81920, FileOptions.DeleteOnClose);
            await request.FileStream.CopyToAsync(tempStream, cancellationToken);
            tempStream.Position = 0;
            originalStream = tempStream;
        }
        else
        {
            // Изображения и файлы до 100 МБ — в оперативной памяти для скорости
            var memStream = new MemoryStream();
            await request.FileStream.CopyToAsync(memStream, cancellationToken);
            memStream.Position = 0;
            originalStream = memStream;
        }

        // Compute SHA256 hash of the file
        // TODO: For better performance with large files, consider computing hash during the initial stream copy
        string fileHash;
        using (var sha256 = SHA256.Create())
        {
            var hashBytes = await sha256.ComputeHashAsync(originalStream, cancellationToken);
            fileHash = Convert.ToHexString(hashBytes).ToLowerInvariant();
        }

        originalStream.Position = 0;

        _logger.LogInformation("Вычислен хеш файла: {FileHash}", fileHash);

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

        originalStream.Position = 0;

        // Серверная дедупликация: проверяем, существует ли файл с таким хешем
        var existingFileId = await _hashesStorage.GetFileIdByHash(fileHash);
        if (existingFileId.HasValue)
        {
            _logger.LogInformation(
                "Файл с хешем {FileHash} уже существует в хранилище (FileId: {ExistingFileId}). Дедупликация.",
                fileHash, existingFileId.Value);

            // Добавляем загрузчиков из текущего запроса к существующему файлу
            foreach (var uploaderId in file.Uploaders)
            {
                await _filesStorage.AddUploaderToFile(existingFileId.Value, uploaderId);
            }

            // Удаляем неиспользуемую запись UploadFile, созданную при GetUploadUrl
            await _filesStorage.DeleteFile(file.Id);

            // Освобождаем поток — загрузка в S3 не требуется
            await originalStream.DisposeAsync();

            return existingFileId.Value.ToString();
        }

        try
        {
            // Принудительное сжатие оригинала изображения по требованиям дизайн-документа
            if (file.Type == UploadFileType.MessageAttachmentImage && contentType.StartsWith("image/"))
            {
                originalStream.Position = 0;

                var (compressedBytes, wasCompressed) =
                    await _imageCompressor.EnforceOriginalLimitsAsync(originalStream);

                if (wasCompressed)
                {
                    _logger.LogInformation(
                        "Изображение {FileId} сжато сервером (превышены лимиты: 2500px / 2МБ). Новый размер: {NewSize} байт",
                        file.Id, compressedBytes!.Length);

                    await originalStream.DisposeAsync();
                    originalStream = new MemoryStream(compressedBytes);
                    fileSize = originalStream.Length;
                    contentType = "image/jpeg";
                }
                else
                {
                    originalStream.Position = 0;
                }
            }

            // Загружаем файл в S3 напрямую из стрима
            var etag = await _s3Uploader.UploadAsync(
                bucketName, // Имя бакета на основе типа файла
                $"{file.Id}", // Используем ID файла как ключ
                originalStream,
                contentType
            );

            _logger.LogInformation("Файл успешно загружен в S3, получен Etag: {Etag}", etag);

            // Если это изображение — сжимаем и сохраняем с другим ключом
            if (_filesToNeedGeneratePreview.Contains(file.Type) && contentType.StartsWith("image/"))
            {
                _logger.LogInformation("Создание превью для изображения с ID {FileId}", file.Id);
                try
                {
                    var previewId = Guid.NewGuid();

                    originalStream.Position = 0;

                    var customWidth = _customFileTypeWidth.GetValueOrDefault(file.Type, 1024);
                    // Сжимаем
                    var compressedBytes = await _imageCompressor.CompressImageAsync(originalStream, customWidth);

                    using var compressedStream = new MemoryStream(compressedBytes);

                    await _s3Uploader.UploadAsync(
                        bucketName,
                        $"{previewId}", // ключ для сжатой версии
                        compressedStream,
                        "image/jpeg" // сохраняем как JPEG
                    );

                    file.PreviewId = previewId;
                    _logger.LogInformation("Превью успешно создано с ID: {PreviewId}", previewId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Ошибка при создании превью для изображения с ID {FileId}", file.Id);
                }

            }

            // Обновляем метаданные файла
            file.Etag = etag;
            file.UploadedAt = DateTime.UtcNow;
            file.Size = fileSize;
        }
        finally
        {
            // Обязательно закрываем поток в конце работы
            await originalStream.DisposeAsync();
        }

        // Сохраняем изменения
        await _filesStorage.UpdateFile(file);

        // Save the file hash for deduplication
        var fileHashEntity = new FileHash
        {
            FileId = file.Id,
            Hash = fileHash
        };
        await _hashesStorage.AddHash(fileHashEntity);

        _logger.LogInformation("Хеш файла сохранен в базу данных");

        _logger.LogInformation("Обработка файла {FileId} успешно завершена", file.Id);

        return file.Id.ToString();
    }

    /// <summary>
    /// Преобразует определённый тип файла в UploadFileType.
    /// Если тип не удалось определить, возвращает MessageAttachmentDocument.
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
            DetectedFileType.Unknown => UploadFileType.MessageAttachmentDocument,
            DetectedFileType.Document => UploadFileType.MessageAttachmentDocument,
            _ => UploadFileType.MessageAttachmentDocument
        };
    }
}