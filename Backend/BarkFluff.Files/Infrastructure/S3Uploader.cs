using Amazon.S3;

namespace BarkFluff.Files.Infrastructure;

public class S3Uploader
{
    private readonly IAmazonS3 _s3Client;

    public S3Uploader(IAmazonS3 s3Client)
    {
        _s3Client = s3Client;
    }

    public async Task<string> UploadAsync(string bucket, string key, Stream data, string contentType)
    {
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
        
        var response = await _s3Client.PutObjectAsync(request);

        return response.ETag;
    }
    
    public async Task<Stream> DownloadAsync(string bucket, string key)
    {
        var request = new Amazon.S3.Model.GetObjectRequest
        {
            BucketName = bucket,
            Key = key
        };
        
        var response = await _s3Client.GetObjectAsync(request);
        
        var memoryStream = new MemoryStream();
        await response.ResponseStream.CopyToAsync(memoryStream);
        memoryStream.Position = 0;
        
        return memoryStream;
    }
}