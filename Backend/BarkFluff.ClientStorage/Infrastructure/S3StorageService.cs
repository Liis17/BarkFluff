using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Transfer;

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
            ServiceURL     = configuration["S3_SERVICE_URL"] ?? "http://localhost:9000",
            ForcePathStyle = true,
            Timeout        = TimeSpan.FromMinutes(30),
            // MinIO/S3-compatible endpoints обычно не поддерживают новые SDK-чексуммы по умолчанию.
            RequestChecksumCalculation  = Amazon.Runtime.RequestChecksumCalculation.WHEN_REQUIRED,
            ResponseChecksumValidation  = Amazon.Runtime.ResponseChecksumValidation.WHEN_REQUIRED
        };

        var accessKey = configuration["S3_ACCESS_KEY"]
            ?? throw new InvalidOperationException("S3_ACCESS_KEY environment variable is required");
        var secretKey = configuration["S3_SECRET_KEY"]
            ?? throw new InvalidOperationException("S3_SECRET_KEY environment variable is required");

        var credentials = new BasicAWSCredentials(accessKey, secretKey);

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

    /// <summary>
    /// Заливка файла в S3 через TransferUtility с автоматическим multipart-разбиением.
    /// На больших файлах (>10MB) надёжнее простого PutObject — устойчиво к таймаутам и кратко-сетевым сбоям.
    /// </summary>
    public async Task UploadAsync(string filePath, string key, string contentType)
    {
        try
        {
            var transferUtility = new TransferUtility(_client);
            var request = new TransferUtilityUploadRequest
            {
                BucketName  = _bucketName,
                Key         = key,
                FilePath    = filePath,
                ContentType = contentType,
                PartSize    = 16 * 1024 * 1024, // 16 MB parts
            };
            await transferUtility.UploadAsync(request);
        }
        catch (AmazonS3Exception s3ex)
        {
            _logger.LogError(s3ex,
                "S3 upload failed: StatusCode={StatusCode} ErrorCode={ErrorCode} RequestId={RequestId} Message={Message}",
                s3ex.StatusCode, s3ex.ErrorCode, s3ex.RequestId, s3ex.Message);
            throw;
        }
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
            // Парсим "bytes=start-end" или "bytes=start-" → ByteRange для SDK
            if (TryParseRange(rangeHeader, out var from, out var to))
            {
                request.ByteRange = to >= 0
                    ? new ByteRange(from, to)
                    : new ByteRange(from, long.MaxValue); // open-ended: до конца объекта
            }
        }

        var response = await _client.GetObjectAsync(request);
        return new S3DownloadResult(response);
    }

    private static bool TryParseRange(string header, out long from, out long to)
    {
        from = 0; to = -1; // -1 означает «до конца объекта»
        // "bytes=1234-5678" или "bytes=1234-"
        if (!header.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase)) return false;
        var span = header.AsSpan(6);
        var dash = span.IndexOf('-');
        if (dash < 0) return false;

        if (!long.TryParse(span[..dash], out from)) return false;

        var toSpan = span[(dash + 1)..];
        if (toSpan.IsEmpty)
        {
            to = -1;
            return true;
        }

        if (!long.TryParse(toSpan, out to)) return false;
        return to >= from;
    }

    public void Dispose()
    {
        _client?.Dispose();
    }
}
