using Barkfluff.AdminPanel.Middleware;
using Barkfluff.AdminPanel.Models;
using Barkfluff.AdminPanel.Services;

namespace Barkfluff.AdminPanel.Endpoints;

public static class S3BrowserEndpoints
{
    public static void MapS3BrowserEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/s3")
            .WithTags("S3 Browser")
            .RequirePermission(AdminPermissions.S3Browse);

        group.MapGet("/buckets", async (
            S3BrowserService s3Service) =>
        {
            try
            {
                var buckets = await s3Service.GetBucketNamesAsync();
                return Results.Ok(buckets);
            }
            catch (Exception ex)
            {
                return Results.Problem($"Ошибка получения списка бакетов: {ex.Message}");
            }
        });

        group.MapGet("/buckets/{bucketId}/objects", async (
            string bucketId,
            S3BrowserService s3Service,
            string? prefix,
            string? continuationToken,
            int? maxKeys) =>
        {
            try
            {
                var result = await s3Service.ListObjectsAsync(
                    bucketId,
                    prefix,
                    continuationToken,
                    maxKeys ?? 100);

                return Results.Ok(result);
            }
            catch (KeyNotFoundException ex)
            {
                return Results.NotFound(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return Results.Problem($"Ошибка получения объектов: {ex.Message}");
            }
        });

        group.MapGet("/buckets/{bucketId}/presign", async (
            string bucketId,
            S3BrowserService s3Service,
            string key) =>
        {
            try
            {
                var url = await s3Service.GetPresignedUrlAsync(bucketId, key);
                return Results.Ok(new { url });
            }
            catch (KeyNotFoundException ex)
            {
                return Results.NotFound(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return Results.Problem($"Ошибка генерации presigned URL: {ex.Message}");
            }
        });
    }
}
