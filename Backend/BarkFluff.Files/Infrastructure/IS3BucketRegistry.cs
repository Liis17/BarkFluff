using Amazon.S3;

using BarkFluff.Files.Domain;

namespace BarkFluff.Files.Infrastructure;

public interface IS3BucketRegistry : IDisposable
{
    string GetBadgeImageBucketName();
    string GetBucketName(UploadFileType fileType);
    IAmazonS3 GetClientForBucket(string bucketName);
    IEnumerable<(string BucketName, IAmazonS3 Client)> GetAllBuckets();
}
