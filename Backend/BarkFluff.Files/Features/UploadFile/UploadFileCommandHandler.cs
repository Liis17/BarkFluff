using BarkFluff.Files.Exceptions;
using BarkFluff.Files.Extensions;
using BarkFluff.Files.Infrastructure;
using BarkFluff.Files.Persistence;
using MediatR;

namespace BarkFluff.Files.Features.UploadFile;

public class UploadFileCommandHandler : IRequestHandler<UploadFileCommand, string>
{
    private readonly UploadedFilesStorage _filesStorage;
    private readonly S3Uploader _s3Uploader;

    public UploadFileCommandHandler(
        UploadedFilesStorage filesStorage,
        S3Uploader s3Uploader)
    {
        _filesStorage = filesStorage;
        _s3Uploader = s3Uploader;
    }

    public async Task<string> Handle(UploadFileCommand request, CancellationToken cancellationToken)
    {
        var file = await _filesStorage.GetFile(request.FileId);
        
        if (file is null)
        {
            throw new Exception("File not found");
        }
        
        // Проверяем, не был ли файл уже загружен
        if (!string.IsNullOrEmpty(file.Etag))
        {
            throw new FileAlreadyUploadedException("Файл уже был загружен");
        }
        
        // Сохраняем имя файла
        file.Filename = request.FileName;
        
        // Определяем тип контента по расширению файла
        var contentType = request.FileName.GetContentType();
        
        // Получаем имя бакета в зависимости от типа файла
        var bucketName = S3BucketHelper.GetBucketName(file.Type);
        
        // Загружаем файл в S3 напрямую из стрима
        var etag = await _s3Uploader.UploadAsync(
            bucketName, // Имя бакета на основе типа файла
            $"{file.Id}", // Используем ID файла как ключ
            request.FileStream,
            contentType
        );
        
        // Обновляем метаданные файла
        file.Etag = etag;
        file.UploadedAt = DateTime.UtcNow;
        
        // Сохраняем изменения
        await _filesStorage.UpdateFile(file);
        
        return file.Id.ToString();
    }
}