namespace BarkFluff.Files.Infrastructure;

public interface IS3Uploader
{
    Task<string> UploadAsync(string bucket, string key, Stream data, string contentType);
    Task<Stream> DownloadAsync(string bucket, string key);

    /// <summary>
    /// Скачать диапазон объекта (этап 3.2): нужен перемотке видео на принимающей ноде —
    /// без Range клиент тянул бы файл целиком ради середины.
    /// </summary>
    /// <param name="rangeFrom">Смещение первого байта, inclusive.</param>
    /// <param name="rangeTo">Граница диапазона, <b>exclusive</b>. null/0 вместе с
    /// <paramref name="rangeFrom"/> = 0 означает «весь объект».</param>
    Task<S3ObjectRange> DownloadRangeAsync(string bucket, string key, long rangeFrom, long? rangeTo);
}

/// <summary>
/// Поток диапазона + метаданные объекта. <see cref="TotalSize"/> — размер объекта ЦЕЛИКОМ
/// (S3 отдаёт его в Content-Range), а не длина выданного куска: принимающая сторона по нему
/// сверяет заявленный размер со снапшотом.
/// </summary>
public sealed record S3ObjectRange(Stream Content, long TotalSize, string? ContentType);
