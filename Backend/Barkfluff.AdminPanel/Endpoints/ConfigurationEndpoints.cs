using Barkfluff.AdminPanel.Models;
using BarkFluff.Proto.Configuration;
using BarkFluff.Shared.Identity;

using Grpc.Core;

namespace Barkfluff.AdminPanel.Endpoints;

/// <summary>
/// Endpoints для управления S3 конфигурацией
/// </summary>
public static class ConfigurationEndpoints
{
    public static void MapConfigurationEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/configuration")
            .WithTags("Configuration");

        // Получить S3 конфигурацию (все бакеты)
        group.MapGet("/s3-configuration", async (
            ConfigurationApi.ConfigurationApiClient configClient,
            HttpContext context) =>
        {
            if (context.Items["AuthToken"] is not AuthToken)
                return Results.Unauthorized();

            try
            {
                var response = await configClient.GetConfigurationAsync(new GetConfigurationRequest
                {
                    ServiceId = (int)ServiceId.Files
                });

                // Группируем конфигурацию по бакетам
                var s3Config = response.Configurations
                    .Where(c => c.Section.StartsWith("S3Buckets:"))
                    .GroupBy(c => c.Section.Replace("S3Buckets:", ""))
                    .ToDictionary(
                        g => g.Key,
                        g => new
                        {
                            serviceUrl = g.FirstOrDefault(c => c.Key == "ServiceUrl")?.Value ?? "",
                            bucketName = g.FirstOrDefault(c => c.Key == "BucketName")?.Value ?? "",
                            accessKey = g.FirstOrDefault(c => c.Key == "AccessKey")?.Value ?? "",
                            secretKey = g.FirstOrDefault(c => c.Key == "SecretKey")?.Value ?? "",
                            editedAt = g.MaxBy(c => c.EditedAt)?.EditedAt?.ToDateTime().ToString("o") ?? DateTime.UtcNow.ToString("o")
                        }
                    );

                return Results.Ok(s3Config);
            }
            catch (Exception ex)
            {
                return Results.Problem($"Ошибка получения конфигурации: {ex.Message}");
            }
        });

        // Обновить S3 конфигурацию для конкретного бакета
        group.MapPost("/s3/update", async (
            ConfigurationApi.ConfigurationApiClient configClient,
            HttpContext context,
            S3BucketUpdateRequest request) =>
        {
            if (context.Items["AuthToken"] is not AuthToken token)
                return Results.Unauthorized();

            try
            {
                var section = $"S3Buckets:{request.BucketId}";
                var results = new List<string>();

                // Обновляем каждое поле
                if (request.Parameters.TryGetValue("serviceUrl", out var serviceUrl))
                {
                    var result = await configClient.UpdateConfigurationAsync(new UpdateConfigurationRequest
                    {
                        Section = section,
                        Key = "ServiceUrl",
                        Value = serviceUrl,
                        ServiceId = (int)ServiceId.Files,
                        EditedBy = token.Name,
                        EditedFrom = context.Connection.RemoteIpAddress?.ToString() ?? "admin-panel"
                    });

                    if (!result.Success)
                        return Results.BadRequest(new { message = result.Message });
                }

                if (request.Parameters.TryGetValue("bucketName", out var bucketName))
                {
                    var result = await configClient.UpdateConfigurationAsync(new UpdateConfigurationRequest
                    {
                        Section = section,
                        Key = "BucketName",
                        Value = bucketName,
                        ServiceId = (int)ServiceId.Files,
                        EditedBy = token.Name,
                        EditedFrom = context.Connection.RemoteIpAddress?.ToString() ?? "admin-panel"
                    });

                    if (!result.Success)
                        return Results.BadRequest(new { message = result.Message });
                }

                if (request.Parameters.TryGetValue("accessKey", out var accessKey))
                {
                    var result = await configClient.UpdateConfigurationAsync(new UpdateConfigurationRequest
                    {
                        Section = section,
                        Key = "AccessKey",
                        Value = accessKey,
                        ServiceId = (int)ServiceId.Files,
                        EditedBy = token.Name,
                        EditedFrom = context.Connection.RemoteIpAddress?.ToString() ?? "admin-panel"
                    });

                    if (!result.Success)
                        return Results.BadRequest(new { message = result.Message });
                }

                if (request.Parameters.TryGetValue("secretKey", out var secretKey))
                {
                    var result = await configClient.UpdateConfigurationAsync(new UpdateConfigurationRequest
                    {
                        Section = section,
                        Key = "SecretKey",
                        Value = secretKey,
                        ServiceId = (int)ServiceId.Files,
                        EditedBy = token.Name,
                        EditedFrom = context.Connection.RemoteIpAddress?.ToString() ?? "admin-panel"
                    });

                    if (!result.Success)
                        return Results.BadRequest(new { message = result.Message });
                }

                return Results.Ok(new { message = $"Бакет {request.BucketId} успешно обновлён" });
            }
            catch (RpcException ex)
            {
                return Results.Problem($"Ошибка gRPC: {ex.Status.Detail}");
            }
            catch (Exception ex)
            {
                return Results.Problem($"Ошибка обновления конфигурации: {ex.Message}");
            }
        });
    }
}

/// <summary>
/// Request модель для обновления S3 бакета
/// </summary>
public class S3BucketUpdateRequest
{
    public string BucketId { get; set; } = string.Empty;
    public Dictionary<string, string> Parameters { get; set; } = new();
}
