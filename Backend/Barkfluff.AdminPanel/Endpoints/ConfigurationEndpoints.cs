using Barkfluff.AdminPanel.Middleware;
using Barkfluff.AdminPanel.Models;
using Barkfluff.AdminPanel.Services;

using BarkFluff.Proto.Configuration;
using BarkFluff.Shared.Identity;

using Grpc.Core;

namespace Barkfluff.AdminPanel.Endpoints;

/// <summary>
/// Endpoints для управления S3 конфигурацией
/// </summary>
public static class ConfigurationEndpoints
{
    private static readonly HashSet<string> SectionOnlyConfigurationSections =
        ["DevelopersDb", "Redis", "NavigatorUrl"];

    public static void MapConfigurationEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/configuration")
            .WithTags("Configuration");

        // Получить все строки конфигурации
        group.MapGet("/all", async (
            ConfigurationApi.ConfigurationApiClient configClient) =>
        {
            try
            {
                var response = await configClient.GetAllConfigurationsAsync(new GetAllConfigurationsRequest());

                var items = response.Configurations.Select(c =>
                {
                    var key = NormalizeConfigurationKey(c.Section, c.Key);
                    var masked = SensitiveConfigMasker.IsSensitive(c.Section, key) && !string.IsNullOrEmpty(c.Value);
                    var field = ConfigurationFieldCatalog.Describe(c.Section, key, c.Value);
                    return new
                    {
                        section = c.Section,
                        key,
                        value = masked ? SensitiveConfigMasker.MaskedValue : c.Value,
                        masked,
                        serviceId = c.ServiceId,
                        serviceName = Enum.IsDefined(typeof(ServiceId), c.ServiceId)
                            ? ((ServiceId)c.ServiceId).ToString()
                            : c.ServiceId.ToString(),
                        editedAt = c.EditedAt?.ToDateTime().ToString("o") ?? "",
                        editedBy = c.EditedBy,
                        editedFrom = c.EditedFrom,
                        fieldType = field.Type.ToString().ToLowerInvariant(),
                        required = field.Required,
                        minimum = field.Minimum,
                        maximum = field.Maximum,
                        hint = field.Hint
                    };
                });

                return Results.Ok(items);
            }
            catch (RpcException ex)
            {
                return Results.Problem($"Ошибка gRPC: {ex.Status.Detail}");
            }
            catch (Exception ex)
            {
                return Results.Problem($"Ошибка получения конфигурации: {ex.Message}");
            }
        })
        .RequirePermission(AdminPermissions.ConfigRead);

        // Обновить значение одной строки конфигурации
        group.MapPost("/update", async (
            ConfigurationApi.ConfigurationApiClient configClient,
            HttpContext context,
            ConfigurationValueUpdateRequest request) =>
        {
            var token = context.GetAuthToken()!;

            var requestValidationError = ValidateConfigurationUpdateRequest(request);
            if (requestValidationError is not null)
                return Results.BadRequest(new { message = requestValidationError });

            if (SensitiveConfigMasker.IsSensitive(request.Section, request.Key) &&
                string.Equals(request.Value, SensitiveConfigMasker.MaskedValue, StringComparison.Ordinal))
            {
                return Results.BadRequest(new { message = "Нельзя сохранить маску вместо реального значения" });
            }

            try
            {
                var configurations = await configClient.GetAllConfigurationsAsync(new GetAllConfigurationsRequest());
                var current = configurations.Configurations.FirstOrDefault(c =>
                    c.Section == request.Section && c.Key == request.Key && c.ServiceId == request.ServiceId);
                var field = ConfigurationFieldCatalog.Describe(
                    request.Section,
                    request.Key,
                    current?.Value ?? request.Value);
                var validationError = ConfigurationFieldCatalog.Validate(field, request.Value);
                if (validationError is not null)
                    return Results.BadRequest(new { message = validationError });

                var result = await configClient.UpdateConfigurationAsync(new UpdateConfigurationRequest
                {
                    Section = request.Section,
                    Key = request.Key,
                    Value = request.Value,
                    ServiceId = request.ServiceId,
                    EditedBy = token.Name,
                    EditedFrom = context.Connection.RemoteIpAddress?.ToString() ?? "admin-panel"
                });

                if (!result.Success)
                    return Results.BadRequest(new { message = result.Message });

                return Results.Ok(new { message = result.Message, editedBy = token.Name });
            }
            catch (RpcException ex)
            {
                return Results.Problem($"Ошибка gRPC: {ex.Status.Detail}");
            }
            catch (Exception ex)
            {
                return Results.Problem($"Ошибка обновления конфигурации: {ex.Message}");
            }
        })
        .RequirePermission(AdminPermissions.ConfigWrite)
        .RequireStepUpFromArguments(StepUpActions.ConfigUpdate, context =>
        {
            var request = context.Arguments.OfType<ConfigurationValueUpdateRequest>().FirstOrDefault();
            return request is null
                ? string.Empty
                : $"serviceId={request.ServiceId};section={request.Section};key={request.Key};valueHash={StepUpService.ComputeParamsHash("config.value", request.Value)}";
        });

        group.MapGet("/history", async (
            ConfigurationApi.ConfigurationApiClient configClient,
            string section,
            string key,
            int serviceId,
            int? count) =>
        {
            try
            {
                var response = await configClient.GetConfigurationHistoryAsync(new GetConfigurationHistoryRequest
                {
                    Section = section,
                    Key = key,
                    ServiceId = serviceId,
                    Count = Math.Clamp(count ?? 30, 1, 100)
                });

                var sensitive = SensitiveConfigMasker.IsSensitive(section, key);
                var revisions = response.Revisions.Select(r => new
                {
                    id = r.Id,
                    section = r.Section,
                    key = r.Key,
                    serviceId = r.ServiceId,
                    previousValue = MaskHistoryValue(r.PreviousValue, sensitive),
                    newValue = MaskHistoryValue(r.NewValue, sensitive),
                    changedAt = r.ChangedAt?.ToDateTime().ToString("o") ?? string.Empty,
                    changedBy = r.ChangedBy,
                    changedFrom = r.ChangedFrom,
                    changeKind = r.ChangeKind,
                    sourceRevisionId = r.SourceRevisionId == 0 ? null : (long?)r.SourceRevisionId,
                    masked = sensitive
                });

                return Results.Ok(revisions);
            }
            catch (RpcException ex)
            {
                return Results.Problem($"Ошибка gRPC: {ex.Status.Detail}");
            }
            catch (Exception ex)
            {
                return Results.Problem($"Ошибка получения истории конфигурации: {ex.Message}");
            }
        })
        .RequirePermission(AdminPermissions.ConfigRead);

        group.MapPost("/rollback", async (
            ConfigurationApi.ConfigurationApiClient configClient,
            HttpContext context,
            ConfigurationRollbackRequest request) =>
        {
            var token = context.GetAuthToken()!;

            try
            {
                var result = await configClient.RollbackConfigurationAsync(new RollbackConfigurationRequest
                {
                    RevisionId = request.RevisionId,
                    EditedBy = token.Name,
                    EditedFrom = context.Connection.RemoteIpAddress?.ToString() ?? "admin-panel"
                });

                return result.Success
                    ? Results.Ok(new { message = result.Message })
                    : Results.BadRequest(new { message = result.Message });
            }
            catch (RpcException ex)
            {
                return Results.Problem($"Ошибка gRPC: {ex.Status.Detail}");
            }
            catch (Exception ex)
            {
                return Results.Problem($"Ошибка отката конфигурации: {ex.Message}");
            }
        })
        .RequirePermission(AdminPermissions.ConfigWrite)
        .RequireStepUpFromArguments(StepUpActions.ConfigRollback, context =>
        {
            var request = context.Arguments.OfType<ConfigurationRollbackRequest>().FirstOrDefault();
            return request is null ? string.Empty : $"revisionId={request.RevisionId}";
        });

        // Получить S3 конфигурацию (все бакеты)
        group.MapGet("/s3-configuration", async (
            ConfigurationApi.ConfigurationApiClient configClient) =>
        {
            try
            {
                var response = await configClient.GetConfigurationAsync(new GetConfigurationRequest
                {
                    ServiceId = (int)ServiceId.Files
                });

                // Группируем конфигурацию по бакетам; секреты не возвращаем — только факт настройки и маску access key
                var s3Config = response.Configurations
                    .Where(c => c.Section.StartsWith("S3Buckets:"))
                    .GroupBy(c => c.Section.Replace("S3Buckets:", ""))
                    .ToDictionary(
                        g => g.Key,
                        g => new
                        {
                            serviceUrl = g.FirstOrDefault(c => c.Key == "ServiceUrl")?.Value ?? "",
                            bucketName = g.FirstOrDefault(c => c.Key == "BucketName")?.Value ?? "",
                            accessKeyConfigured = !string.IsNullOrEmpty(g.FirstOrDefault(c => c.Key == "AccessKey")?.Value),
                            accessKeyMasked = SensitiveConfigMasker.MaskAccessKey(g.FirstOrDefault(c => c.Key == "AccessKey")?.Value ?? ""),
                            secretKeyConfigured = !string.IsNullOrEmpty(g.FirstOrDefault(c => c.Key == "SecretKey")?.Value),
                            region = g.FirstOrDefault(c => c.Key == "Region")?.Value ?? "",
                            editedAt = g.MaxBy(c => c.EditedAt)?.EditedAt?.ToDateTime().ToString("o") ?? DateTime.UtcNow.ToString("o")
                        }
                    );

                return Results.Ok(s3Config);
            }
            catch (Exception ex)
            {
                return Results.Problem($"Ошибка получения конфигурации: {ex.Message}");
            }
        })
        .RequirePermission(AdminPermissions.ConfigRead);

        // Обновить S3 конфигурацию для конкретного бакета.
        // Пустые/отсутствующие accessKey и secretKey означают «оставить текущее значение» (замена секрета — write-only).
        group.MapPost("/s3/update", async (
            ConfigurationApi.ConfigurationApiClient configClient,
            S3BrowserService s3Browser,
            HttpContext context,
            S3BucketUpdateRequest request) =>
        {
            var token = context.GetAuthToken()!;

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

                if (request.Parameters.TryGetValue("accessKey", out var accessKey) && !string.IsNullOrWhiteSpace(accessKey))
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

                if (request.Parameters.TryGetValue("secretKey", out var secretKey) && !string.IsNullOrWhiteSpace(secretKey))
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

                if (request.Parameters.TryGetValue("region", out var region))
                {
                    var result = await configClient.UpdateConfigurationAsync(new UpdateConfigurationRequest
                    {
                        Section = section,
                        Key = "Region",
                        Value = region,
                        ServiceId = (int)ServiceId.Files,
                        EditedBy = token.Name,
                        EditedFrom = context.Connection.RemoteIpAddress?.ToString() ?? "admin-panel"
                    });

                    if (!result.Success)
                        return Results.BadRequest(new { message = result.Message });
                }

                s3Browser.InvalidateCache(request.BucketId);

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
        })
        .RequirePermission(AdminPermissions.ConfigWrite)
        .RequireStepUpFromArguments(StepUpActions.S3ConfigUpdate, context =>
        {
            var request = context.Arguments.OfType<S3BucketUpdateRequest>().FirstOrDefault();
            if (request is null)
                return string.Empty;

            var values = request.Parameters
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => $"{pair.Key}Hash={StepUpService.ComputeParamsHash("s3.value", pair.Value)}");
            return $"bucket={request.BucketId};{string.Join(";", values)}";
        });
    }

    internal static string? ValidateConfigurationUpdateRequest(ConfigurationValueUpdateRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Section))
            return "Section обязательна";

        if (request.Key is null ||
            (!string.IsNullOrEmpty(request.Key) && string.IsNullOrWhiteSpace(request.Key)))
            return "Key должен быть пустым или содержать непробельные символы";

        return null;
    }

    internal static string NormalizeConfigurationKey(string section, string? key) =>
        key is null || (SectionOnlyConfigurationSections.Contains(section) && string.IsNullOrWhiteSpace(key))
            ? string.Empty
            : key;

    private static string MaskHistoryValue(string value, bool sensitive) =>
        sensitive && !string.IsNullOrEmpty(value) ? SensitiveConfigMasker.MaskedValue : value;
}

/// <summary>
/// Request модель для обновления значения строки конфигурации
/// </summary>
public class ConfigurationValueUpdateRequest
{
    public string Section { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public int ServiceId { get; set; }
    public string Value { get; set; } = string.Empty;
}

public class ConfigurationRollbackRequest
{
    public long RevisionId { get; set; }
}

/// <summary>
/// Request модель для обновления S3 бакета
/// </summary>
public class S3BucketUpdateRequest
{
    public string BucketId { get; set; } = string.Empty;
    public Dictionary<string, string> Parameters { get; set; } = new();
}
