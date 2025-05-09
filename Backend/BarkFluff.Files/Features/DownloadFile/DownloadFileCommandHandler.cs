using BarkFluff.Files.Exceptions;
using BarkFluff.Files.Extensions;
using BarkFluff.Files.Infrastructure;
using BarkFluff.Files.Persistence;
using MediatR;

namespace BarkFluff.Files.Features.DownloadFile;

public class DownloadFileCommandHandler : IRequestHandler<DownloadFileCommand, DownloadFileResult>
{
    private readonly UploadedFilesStorage _filesStorage;
    private readonly S3Uploader _s3Uploader;

    public DownloadFileCommandHandler(
        UploadedFilesStorage filesStorage,
        S3Uploader s3Uploader)
    {
        _filesStorage = filesStorage;
        _s3Uploader = s3Uploader;
    }

    public async Task<DownloadFileResult> Handle(DownloadFileCommand request, CancellationToken cancellationToken)
    {
        var file = await _filesStorage.GetFile(request.FileId);
        
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
            FileName = file.Filename,
            ContentType = contentType
        };
    }
}
