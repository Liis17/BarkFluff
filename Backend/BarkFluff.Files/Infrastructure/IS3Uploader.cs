namespace BarkFluff.Files.Infrastructure;

public interface IS3Uploader
{
    Task<string> UploadAsync(string bucket, string key, Stream data, string contentType);
    Task<Stream> DownloadAsync(string bucket, string key);
}
