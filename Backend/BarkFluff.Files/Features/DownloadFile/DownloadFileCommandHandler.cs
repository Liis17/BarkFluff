using BarkFluff.Files.Domain;
using BarkFluff.Files.Exceptions;
using BarkFluff.Files.Extensions;
using BarkFluff.Files.Infrastructure;
using BarkFluff.Files.Persistence;
using MediatR;
using Microsoft.AspNetCore.Http.HttpResults;

namespace BarkFluff.Files.Features.DownloadFile;

public class DownloadFileCommandHandler : IRequestHandler<DownloadFileCommand, DownloadFileResult>
{
    private readonly UploadedFilesStorage _filesStorage;
    private readonly S3Uploader _s3Uploader;
    private readonly TempFilesStorage _tempFilesStorage;

    public DownloadFileCommandHandler(UploadedFilesStorage filesStorage, S3Uploader s3Uploader,
        TempFilesStorage tempFilesStorage)
    {
        _filesStorage = filesStorage;
        _s3Uploader = s3Uploader;
        _tempFilesStorage = tempFilesStorage;
    }

    public async Task<DownloadFileResult> Handle(DownloadFileCommand request, CancellationToken cancellationToken)
    {
        var file = await _filesStorage.GetFile(request.FileId);

        // Незя получать файлы по их оригинальным ID кроме аватарок и картинок чата
        if (file is { Type: not (UploadFileType.UserAvatar or UploadFileType.ChatPicture) })
        {
            throw new Exception("Файл не найден");
        }
        
        // Это временная ссылка
        if (file is null)
        {
            var tempFile = await _tempFilesStorage.GetTempFile(request.FileId);

            if (tempFile != null)
            {
                file = await _filesStorage.GetFile(tempFile.OriginalFileId);
            }
        }

        // Это ссылка на превью
        if (file is null)
        {
            file = await _filesStorage.GetFileByPreviewId(request.FileId);

            if (file != null)
            {
                file.Id = file.PreviewId!.Value;
            }
        }

        // Если все ещё не нашли
        if (file is null)
        {
            throw new Exception("Файл не найден");
        }
        
        if (string.IsNullOrEmpty(file.Etag))
        {
            throw new FileNotUploadedException("Файл ещё не был загружен");
        }
        
        // Определяем тип контента по расширению файла
        var contentType = file.Filename.GetContentType();
        
        var extension = Path.GetExtension(file.Filename).ToLowerInvariant();
        
        // Получаем имя бакета в зависимости от типа файла
        var bucketName = S3BucketHelper.GetBucketName(file.Type);
        
        // Скачиваем файл из S3
        var fileStream = await _s3Uploader.DownloadAsync(
            bucketName, // Имя бакета на основе типа файла
            $"{file.Id}" // Используем ID файла как ключ
        );
        
        return new DownloadFileResult
        {
            FileStream = fileStream,
            FileName = $"{file.Id}{extension}",
            ContentType = contentType
        };
    }
}
