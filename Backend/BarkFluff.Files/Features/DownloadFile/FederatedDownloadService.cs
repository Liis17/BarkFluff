using BarkFluff.Files.Domain;
using BarkFluff.Files.Extensions;
using BarkFluff.Files.Helpers;
using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Proto.FederationInternal;

using Grpc.Core;

using Microsoft.AspNetCore.Http;

namespace BarkFluff.Files.Features.DownloadFile;

/// <summary>
/// Скачивание federated-вложения через свою ноду (этап 3.3): байты идут с origin
/// потоком и сразу уходят клиенту — ни на диск, ни в память целиком они не попадают.
/// </summary>
/// <remarks>
/// Кеша содержимого и превью нет — решение владельца (см. docs/rearch/phase-3/README.md).
/// Каждое обращение тянет байты с origin заново.
/// </remarks>
public class FederatedDownloadService
{
    private readonly FederationInternalApi.FederationInternalApiClient _federationClient;
    private readonly MetricsCollector _metrics;
    private readonly ILogger<FederatedDownloadService> _logger;
    private readonly int _retryAfterSeconds;

    public FederatedDownloadService(
        FederationInternalApi.FederationInternalApiClient federationClient,
        IConfiguration configuration,
        MetricsCollector metrics,
        ILogger<FederatedDownloadService> logger)
    {
        _federationClient = federationClient;
        _metrics = metrics;
        _logger = logger;

        var raw = configuration["Files:FedRetryAfterSeconds"];
        _retryAfterSeconds = int.TryParse(raw, out var parsed) && parsed > 0 ? parsed : 30;
    }

    /// <summary>
    /// Аватар remote-пользователя (этап 3.4): снапшота размера нет — вместо него глобальный кап.
    /// </summary>
    public async Task WriteAvatarToResponseAsync(
        string serverName,
        Guid fileId,
        long maxBytes,
        HttpContext httpContext)
    {
        await WriteToResponseAsync(
            new TempFile
            {
                OriginalFileId = fileId,
                OriginServer = serverName,
                // Размер неизвестен: Range по аватару не нужен, а объём ограничивает кап.
                SizeBytes = null,
            },
            httpContext,
            hardLimitBytes: maxBytes);
    }

    /// <summary>
    /// Записать содержимое (или запрошенный диапазон) прямо в ответ. Заголовки выставляются
    /// здесь же: хелпер <c>File()</c> не подходит — поток не seekable.
    /// </summary>
    public async Task<FederatedDownloadResult> WriteToResponseAsync(
        TempFile tempFile,
        HttpContext httpContext,
        long? hardLimitBytes = null)
    {
        var response = httpContext.Response;
        var totalSize = tempFile.SizeBytes ?? 0;

        var rangeStatus = ByteRangeHeader.TryParse(
            httpContext.Request.Headers.Range, totalSize, out var range);

        if (rangeStatus == ByteRangeHeader.Status.Unsatisfiable)
        {
            response.StatusCode = StatusCodes.Status416RangeNotSatisfiable;
            response.Headers.ContentRange = $"bytes */{totalSize}";
            return new FederatedDownloadResult(false, 0);
        }

        var isPartial = rangeStatus == ByteRangeHeader.Status.Satisfiable;

        // Верхняя граница нашего контракта exclusive; 0/0 = весь файл.
        var request = new FetchRemoteFileRequest
        {
            ServerName = tempFile.OriginServer!,
            FileId = tempFile.OriginalFileId.ToString(),
            RangeFrom = isPartial ? range.From : 0,
            RangeTo = isPartial ? range.To : 0,
        };

        using var call = _federationClient.FetchRemoteFile(
            request, cancellationToken: httpContext.RequestAborted);

        var headersSent = false;
        long written = 0;

        // Заголовки пишем только после первого чанка: пока ответ не начат, ошибку ещё можно
        // отдать честным кодом (503/404), а не оборванным телом (этап 3.5).
        try
        {
            // Сколько байт мы вообще готовы принять: снапшот из Messages — более строгая граница,
            // чем заявленный origin'ом total_size (Federation режет по нему, этап 3.2).
            // У аватара снапшота нет, вместо него — глобальный кап (этап 3.4).
            var limit = isPartial ? range.Length : totalSize;
            if (hardLimitBytes is > 0 && (limit <= 0 || hardLimitBytes < limit))
            {
                limit = hardLimitBytes.Value;
            }

            await foreach (var chunk in call.ResponseStream.ReadAllAsync(httpContext.RequestAborted))
            {
            if (!headersSent)
            {
                WriteHeaders(response, tempFile, chunk.ContentType, isPartial, range, totalSize);
                headersSent = true;
            }

            if (chunk.Data.IsEmpty)
            {
                continue;
            }

            written += chunk.Data.Length;

            // Отсечение по снапшоту (риск №44, второй уровень): origin не может прислать
            // больше, чем мы записали у себя при импорте сообщения.
            if (limit > 0 && written > limit)
            {
                _metrics.Increment("fed_download_size_exceeded");
                _logger.LogWarning(
                    "Origin {Origin} прислал больше байт ({Written}), чем допускает снапшот ({Limit}) для {FileId}",
                    tempFile.OriginServer, written, limit, tempFile.OriginalFileId);

                // Заголовки уже ушли — корректного кода ошибки не осталось, рвём соединение.
                httpContext.Abort();
                return new FederatedDownloadResult(false, written);
            }

            chunk.Data.WriteTo(response.Body);
            await response.Body.FlushAsync(httpContext.RequestAborted);
        }

            if (!headersSent)
            {
                WriteHeaders(response, tempFile, contentType: null, isPartial, range, totalSize);
            }

            _metrics.Increment("fed_download_total.ok");
            _metrics.Increment("fed_downloads");
            _metrics.Add("fed_download_bytes_total", written);
            return new FederatedDownloadResult(true, written);
        }
        catch (RpcException ex) when (!headersSent)
        {
            WriteErrorStatus(response, ex, tempFile);
            return new FederatedDownloadResult(false, written);
        }
        catch (RpcException)
        {
            // Ответ уже начат — корректного кода не осталось. Клиент увидит truncated body
            // и ретраит с Range (докачка).
            _metrics.Increment("fed_download_total.aborted");
            httpContext.Abort();
            return new FederatedDownloadResult(false, written);
        }
    }

    /// <summary>
    /// Карта gRPC-ошибок Federation в HTTP (этап 3.5). Едина для обеих fed-веток —
    /// вложений (3.3) и аватаров (3.4).
    /// </summary>
    /// <remarks>
    /// <c>PermissionDenied</c> и <c>NotFound</c> намеренно сливаются в 404: capability-модель
    /// не должна светить причину отказа. <c>Unavailable</c> — единственный «временный» код,
    /// и только он получает <c>Retry-After</c>.
    /// </remarks>
    private void WriteErrorStatus(HttpResponse response, RpcException ex, TempFile tempFile)
    {
        if (ex.StatusCode == StatusCode.Unavailable)
        {
            response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            response.Headers.RetryAfter = _retryAfterSeconds.ToString();

            _metrics.Increment("fed_download_total.origin_unavailable");
            _logger.LogWarning(
                "Origin {Origin} недоступен при скачивании {FileId}: {Detail}",
                tempFile.OriginServer, tempFile.OriginalFileId, ex.Status.Detail);
            return;
        }

        // Отказ живой ноды (нет общего чата, приватный аватар) либо файла нет — окончательно.
        response.StatusCode = StatusCodes.Status404NotFound;

        _metrics.Increment("fed_download_total.denied");
        _logger.LogWarning(
            "Origin {Origin} отказал в файле {FileId}: {Code}",
            tempFile.OriginServer, tempFile.OriginalFileId, ex.StatusCode);
    }

    private static void WriteHeaders(
        HttpResponse response,
        TempFile tempFile,
        string? contentType,
        bool isPartial,
        ByteRangeHeader.Result range,
        long totalSize)
    {
        response.StatusCode = isPartial
            ? StatusCodes.Status206PartialContent
            : StatusCodes.Status200OK;

        // Content-Type: с origin (он знает реальный тип из S3), иначе — по имени из снапшота.
        response.ContentType = !string.IsNullOrEmpty(contentType)
            ? contentType
            : (tempFile.FileName ?? string.Empty).GetContentType();

        response.Headers.AcceptRanges = "bytes";

        if (isPartial)
        {
            response.Headers.ContentRange = $"bytes {range.From}-{range.To - 1}/{totalSize}";
            response.ContentLength = range.Length;
        }
        else if (totalSize > 0)
        {
            response.ContentLength = totalSize;
        }

        var fileName = SanitizeFileName(tempFile.FileName);
        if (fileName.Length > 0)
        {
            // Имя пришло с чужой ноды — в заголовок оно попадает только после санитизации.
            response.Headers.ContentDisposition = $"attachment; filename=\"{fileName}\"";
        }
    }

    /// <summary>
    /// Имя файла приходит с чужой ноды: убираем путь (traversal) и всё, что может разорвать
    /// заголовок (CR/LF, кавычки, управляющие символы).
    /// </summary>
    private static string SanitizeFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return string.Empty;
        }

        var baseName = Path.GetFileName(fileName.Replace('\\', '/'));

        var sanitized = new string(baseName
            .Where(c => !char.IsControl(c) && c != '"' && c != '\r' && c != '\n')
            .ToArray())
            .Trim();

        // "." и ".." после GetFileName — не имена, а остатки traversal-попытки.
        return sanitized is "." or ".." ? string.Empty : sanitized;
    }
}

public readonly record struct FederatedDownloadResult(bool Completed, long BytesWritten);
