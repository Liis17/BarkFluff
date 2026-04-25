using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;

namespace BarkFluff.ClientStorage.Infrastructure;

public sealed class S3DownloadResult : IDisposable
{
    private readonly GetObjectResponse _response;

    public Stream Stream        => _response.ResponseStream;
    public string ContentType   => _response.Headers.ContentType;
    public long   ContentLength => _response.ContentLength;

    internal S3DownloadResult(GetObjectResponse response)
    {
        _response = response;
    }

    public void Dispose() => _response.Dispose();
}

public class S3StorageService : IDisposable
{
    private readonly IAmazonS3 _client;
    private readonly string _bucketName;
    private readonly ILogger<S3StorageService> _logger;

    public S3StorageService(IConfiguration configuration, ILogger<S3StorageService> logger)
    {
        _logger = logger;
        _bucketName = configuration["S3_BUCKET_NAME"] ?? "client-storage";

        var config = new AmazonS3Config
        {
            ServiceURL = configuration["S3_SERVICE_URL"] ?? "http://localhost:9000",
            ForcePathStyle = true
        };

        var credentials = new BasicAWSCredentials(
            configuration["S3_ACCESS_KEY"] ?? "",
            configuration["S3_SECRET_KEY"] ?? "");

        _client = new AmazonS3Client(credentials, config);
    }

    public async Task InitializeBucketAsync()
    {
        try
        {
            await _client.GetBucketLocationAsync(_bucketName);
            _logger.LogInformation("Бакет {BucketName} уже существует", _bucketName);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogInformation("Создаём бакет {BucketName}", _bucketName);
            await _client.PutBucketAsync(new PutBucketRequest { BucketName = _bucketName });
        }
    }

    public async Task<string> UploadAsync(string key, Stream data, string contentType)
    {
        var request = new PutObjectRequest
        {
            BucketName = _bucketName,
            Key = key,
            InputStream = data,
            AutoCloseStream = false,
            AutoResetStreamPosition = false,
            ContentType = contentType
        };

        var response = await _client.PutObjectAsync(request);
        return response.ETag;
    }

    /// <summary>
    /// Возвращает стриминговый ответ S3 без буферизации.
    /// Caller должен через await using освободить результат.
    /// Поддерживает Range-запросы для возобновляемых загрузок / BITS.
    /// </summary>
    public async Task<S3DownloadResult> DownloadAsync(string key, string? rangeHeader = null)
    {
        var request = new GetObjectRequest
        {
            BucketName = _bucketName,
            Key = key
        };

        if (!string.IsNullOrEmpty(rangeHeader))
        {
            // Парсим "bytes=start-end" → ByteRange для SDK
            if (TryParseRange(rangeHeader, out var from, out var to))
                request.ByteRange = new ByteRange(from, to);
        }

        var response = await _client.GetObjectAsync(request);
        return new S3DownloadResult(response);
    }

    private static bool TryParseRange(string header, out long from, out long to)
    {
        from = 0; to = 0;
        // "bytes=1234-5678"
        if (!header.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase)) return false;
        var span = header.AsSpan(6);
        var dash = span.IndexOf('-');
        if (dash < 0) return false;
        return long.TryParse(span[..dash], out from)
            && long.TryParse(span[(dash + 1)..], out to)
            && to >= from;
    }

    public void Dispose()
    {
        _client?.Dispose();
    }
}
