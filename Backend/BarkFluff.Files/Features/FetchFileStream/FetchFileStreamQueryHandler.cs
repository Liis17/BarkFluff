using BarkFluff.Files.Extensions;
using BarkFluff.Files.Infrastructure;
using BarkFluff.Files.Persistence;

using Grpc.Core;

namespace BarkFluff.Files.Features.FetchFileStream;

/// <summary>
/// Отдаёт содержимое файла чанками (этап 3.2). Вызывает только Federation своей ноды с
/// service-токеном, уже выполнив авторизацию на уровне ноды
/// (<c>Messages.CheckFileFederationAccess</c>), поэтому whitelist типов, действующий на публичном
/// <c>/download/{id}</c>, здесь намеренно не применяется — иначе вложения нельзя было бы отдать вовсе.
/// </summary>
public class FetchFileStreamQueryHandler
{
    /// <summary>
    /// 256 КБ — заметно ниже лимита gRPC-сообщения (4 МБ) с запасом на накладные расходы,
    /// и при этом достаточно крупно, чтобы не захлёбываться на числе сообщений.
    /// </summary>
    public const int ChunkSize = 256 * 1024;

    private readonly UploadedFilesStorage _filesStorage;
    private readonly IS3Uploader _s3Uploader;
    private readonly IS3BucketRegistry _bucketRegistry;
    private readonly ILogger<FetchFileStreamQueryHandler> _logger;

    public FetchFileStreamQueryHandler(
        UploadedFilesStorage filesStorage,
        IS3Uploader s3Uploader,
        IS3BucketRegistry bucketRegistry,
        ILogger<FetchFileStreamQueryHandler> logger)
    {
        _filesStorage = filesStorage;
        _s3Uploader = s3Uploader;
        _bucketRegistry = bucketRegistry;
        _logger = logger;
    }

    public async Task HandleAsync(
        FetchFileStreamQuery request,
        IServerStreamWriter<Proto.Files.FileStreamChunk> responseStream,
        CancellationToken cancellationToken)
    {
        var file = await _filesStorage.GetFile(request.FileId);

        if (file is null || string.IsNullOrEmpty(file.Etag))
        {
            // Не различаем «нет файла» и «не загружен» — наружу это одинаковый NotFound.
            _logger.LogDebug("FetchFileStream: файл {FileId} не найден или не загружен", request.FileId);
            throw new RpcException(new Status(StatusCode.NotFound, "файл не найден"));
        }

        var bucketName = _bucketRegistry.GetBucketName(file.Type);
        var contentType = file.Filename.GetContentType();

        var range = await _s3Uploader.DownloadRangeAsync(
            bucketName,
            file.Id.ToString(),
            request.RangeFrom,
            request.RangeTo > 0 ? request.RangeTo : null);

        await using var content = range.Content;

        // Первый чанк — метаданные: принимающая сторона по total_size контролирует объём
        // и обрывает стрим при превышении (защита №44).
        await responseStream.WriteAsync(new Proto.Files.FileStreamChunk
        {
            TotalSize = range.TotalSize,
            ContentType = string.IsNullOrEmpty(range.ContentType) ? contentType : range.ContentType,
            FileName = file.Filename ?? string.Empty,
        }, cancellationToken);

        var buffer = new byte[ChunkSize];
        long sent = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var read = await content.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            await responseStream.WriteAsync(new Proto.Files.FileStreamChunk
            {
                Data = Google.Protobuf.ByteString.CopyFrom(buffer, 0, read),
            }, cancellationToken);

            sent += read;
        }

        _logger.LogDebug(
            "FetchFileStream: файл {FileId} отдан, {Sent} байт (range {From}-{To})",
            file.Id, sent, request.RangeFrom, request.RangeTo);
    }
}
