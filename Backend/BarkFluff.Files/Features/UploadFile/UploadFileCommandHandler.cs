using System.Security.Cryptography;
using BarkFluff.Files.Domain;
using BarkFluff.Files.Exceptions;
using BarkFluff.Files.Extensions;
using BarkFluff.Files.Infrastructure;
using BarkFluff.Files.Persistence;
using BarkFluff.Files.Services;
using MediatR;

using UploadFileType = BarkFluff.Files.Domain.UploadFileType;

namespace BarkFluff.Files.Features.UploadFile;

public class UploadFileCommandHandler : IRequestHandler<UploadFileCommand, string>
{
    private readonly UploadedFilesStorage _filesStorage;
    private readonly FileHashesStorage _hashesStorage;
    private readonly S3Uploader _s3Uploader;
    private readonly S3BucketRegistry _bucketRegistry;
    private readonly ImageCompressor _imageCompressor;
    private readonly ILogger<UploadFileCommandHandler> _logger;


    private readonly List<UploadFileType> _filesToNeedGeneratePreview
        = [UploadFileType.ChatPicture, UploadFileType.MessageAttachmentImage, UploadFileType.UserAvatar];

    private readonly Dictionary<UploadFileType, int> _customFileTypeWidth = new()
    {
        { UploadFileType.UserAvatar, 64 }
    };

    public UploadFileCommandHandler(
        UploadedFilesStorage filesStorage,
        FileHashesStorage hashesStorage,
        S3Uploader s3Uploader,
        S3BucketRegistry bucketRegistry,
        ImageCompressor imageCompressor,
        ILogger<UploadFileCommandHandler> logger)
    {
        _filesStorage = filesStorage;
        _hashesStorage = hashesStorage;
        _s3Uploader = s3Uploader;
        _bucketRegistry = bucketRegistry;
        _imageCompressor = imageCompressor;
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
        
        long fileSize = request.FileStream.Length;

        var originalStream = new MemoryStream();
        await request.FileStream.CopyToAsync(originalStream, cancellationToken);
        
        originalStream.Position = 0;
        
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
        
        try
        {
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
}