using BarkFluff.GrpcServer.Settings;
using Microsoft.Extensions.Configuration;

namespace BarkFluff.Files.Helpers;

public static class FileUrlHelper
{
    /// <summary>
    /// Вычисляет публичный базовый URL для HTTP-эндпоинтов файлового сервиса.
    /// Через nginx: ExternalEndpoint:Host + /web (nginx маршрутизирует /web/ → files:7006)
    /// Локально: RunSettings.Host:Http1Port (прямой доступ без nginx)
    /// </summary>
    public static string GetPublicBaseUrl(IConfiguration configuration, RunSettings runSettings)
    {
        var externalHost = configuration["ExternalEndpoint:Host"];
        if (!string.IsNullOrWhiteSpace(externalHost))
            return $"{externalHost.TrimEnd('/')}/web";

        // Фолбэк для локальной разработки без nginx
        if (!string.IsNullOrWhiteSpace(runSettings.Host))
            return $"{runSettings.Host.TrimEnd('/')}:{runSettings.Http1Port}";

        return $"http://localhost:{runSettings.Http1Port}";
    }

    public static string GenerateUploadUrl(string baseUrl, Guid fileId)
    {
        return $"{baseUrl}/upload/{fileId}";
    }

    public static string GenerateDownloadUrl(string baseUrl, Guid fileId)
    {
        return $"{baseUrl}/download/{fileId}";
    }
}
