using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;

namespace BarkFluff.ClientStorage.Infrastructure;

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

    public async Task<(Stream Stream, string ContentType)> DownloadAsync(string key)
    {
        var request = new GetObjectRequest
        {
            BucketName = _bucketName,
            Key = key
        };

        var response = await _client.GetObjectAsync(request);

        var memoryStream = new MemoryStream();
        await response.ResponseStream.CopyToAsync(memoryStream);
        memoryStream.Position = 0;

        return (memoryStream, response.Headers.ContentType);
    }

    public void Dispose()
    {
        _client?.Dispose();
    }
}
