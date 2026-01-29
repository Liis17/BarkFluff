using BarkFluff.Files.Domain;
using BarkFluff.Files.Exceptions;
using BarkFluff.Files.Extensions;
using BarkFluff.Files.Infrastructure;
using BarkFluff.Files.Persistence;
using MediatR;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging;

namespace BarkFluff.Files.Features.DownloadFile;

public class DownloadFileCommandHandler : IRequestHandler<DownloadFileCommand, DownloadFileResult>
{
    private readonly UploadedFilesStorage _filesStorage;
    private readonly S3Uploader _s3Uploader;
    private readonly S3BucketRegistry _bucketRegistry;
    private readonly TempFilesStorage _tempFilesStorage;
    private readonly ILogger<DownloadFileCommandHandler> _logger;

    public DownloadFileCommandHandler(UploadedFilesStorage filesStorage, S3Uploader s3Uploader,
        S3BucketRegistry bucketRegistry, TempFilesStorage tempFilesStorage,
        ILogger<DownloadFileCommandHandler> logger)
    {
        _filesStorage = filesStorage;
        _s3Uploader = s3Uploader;
        _bucketRegistry = bucketRegistry;
        _tempFilesStorage = tempFilesStorage;
        _logger = logger;
    }

    public async Task<DownloadFileResult> Handle(DownloadFileCommand request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Запрос на скачивание файла {FileId}", request.FileId);

        var file = await _filesStorage.GetFile(request.FileId);

        // Незя получать файлы по их оригинальным ID кроме аватарок и картинок чата
        if (file is { Type: not (UploadFileType.UserAvatar or UploadFileType.ChatPicture) })
        {
            _logger.LogWarning(
                "Попытка доступа к файлу {FileId} с недопустимым типом {FileType}",
                request.FileId,
                file.Type
            );
            throw new Exception("Файл не найден");
        }

        // Это временная ссылка
        if (file is null)
        {
            _logger.LogDebug("Поиск временной ссылки для {FileId}", request.FileId);
            var tempFile = await _tempFilesStorage.GetTempFile(request.FileId);

            if (tempFile != null)
            {
                _logger.LogDebug(
                    "Найдена временная ссылка {TempFileId} -> оригинальный файл {OriginalFileId}",
                    request.FileId,
                    tempFile.OriginalFileId
                );
                file = await _filesStorage.GetFile(tempFile.OriginalFileId);
            }
        }

        // Это ссылка на превью
        if (file is null)
        {
            _logger.LogDebug("Поиск превью для {FileId}", request.FileId);
            file = await _filesStorage.GetFileByPreviewId(request.FileId);

            if (file != null)
            {
                _logger.LogDebug("Найдено превью для файла {OriginalFileId}", file.Id);
                file.Id = file.PreviewId!.Value;
            }
        }

        // Если все ещё не нашли
        if (file is null)
        {
            _logger.LogWarning("Файл {FileId} не найден", request.FileId);
            throw new Exception("Файл не найден");
        }

        if (string.IsNullOrEmpty(file.Etag))
        {
            _logger.LogWarning("Файл {FileId} ещё не был загружен", file.Id);
            throw new FileNotUploadedException("Файл ещё не был загружен");
        }

        // Определяем тип контента по расширению файла
        var contentType = file.Filename.GetContentType();

        var extension = Path.GetExtension(file.Filename).ToLowerInvariant();

        // Получаем имя бакета в зависимости от типа файла
        var bucketName = _bucketRegistry.GetBucketName(file.Type);

        _logger.LogDebug(
            "Скачивание файла {FileId} из S3. Bucket: {BucketName}, Размер: {FileSize} байт",
            file.Id,
            bucketName,
            file.Size
        );

        // Скачиваем файл из S3
        var fileStream = await _s3Uploader.DownloadAsync(
            bucketName, // Имя бакета на основе типа файла
            $"{file.Id}" // Используем ID файла как ключ
        );

        _logger.LogInformation(
            "Файл {FileId} ({FileName}) успешно скачан. Тип: {FileType}, Размер: {FileSize} байт",
            file.Id,
            file.Filename,
            file.Type,
            file.Size
        );

        return new DownloadFileResult
        {
            FileStream = fileStream,
            FileName = $"{file.Id}{extension}",
            ContentType = contentType
        };
    }
}
