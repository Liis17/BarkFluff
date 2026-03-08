namespace BarkFluff.Files.Infrastructure;

public class S3Uploader
{
    private readonly S3BucketRegistry _registry;

    public S3Uploader(S3BucketRegistry registry)
    {
        _registry = registry;
    }

    public async Task<string> UploadAsync(string bucket, string key, Stream data, string contentType)
    {
        var client = _registry.GetClientForBucket(bucket);

        var request = new Amazon.S3.Model.PutObjectRequest
        {
            BucketName = bucket,
            Key = key,
            InputStream = data,
            AutoCloseStream = false,
            AutoResetStreamPosition = false,
            ContentType = contentType,
            Metadata = { ["original-filename"] = Path.GetFileName(key) }
        };

        var response = await client.PutObjectAsync(request);

        return response.ETag;
    }

    public async Task<Stream> DownloadAsync(string bucket, string key)
    {
        var client = _registry.GetClientForBucket(bucket);

        var request = new Amazon.S3.Model.GetObjectRequest
        {
            BucketName = bucket,
            Key = key
        };

        var response = await client.GetObjectAsync(request);

        var memoryStream = new MemoryStream();
        await response.ResponseStream.CopyToAsync(memoryStream);
        memoryStream.Position = 0;

        return memoryStream;
    }
}
