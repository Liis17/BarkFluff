using BarkFluff.Files.Exceptions;
using BarkFluff.Files.Features.DownloadFile;
using BarkFluff.Files.Features.UploadFile;
using BarkFluff.Files.Persistence;
using BarkFluff.Proto.Users;
using BarkFluff.GrpcServer.Metrics;

using MediatR;

using Microsoft.AspNetCore.Mvc;

namespace BarkFluff.Files.Host;

public class FilesController : Controller
{
    private readonly IMediator _mediator;
    private readonly TempFilesStorage _tempFilesStorage;
    private readonly FederatedDownloadService _federatedDownload;
    private readonly UsersServerApi.UsersServerApiClient _usersClient;
    private readonly IConfiguration _configuration;
    private readonly MetricsCollector _metrics;
    private readonly ILogger<FilesController> _logger;

    public FilesController(
        IMediator mediator,
        TempFilesStorage tempFilesStorage,
        FederatedDownloadService federatedDownload,
        UsersServerApi.UsersServerApiClient usersClient,
        IConfiguration configuration,
        MetricsCollector metrics,
        ILogger<FilesController> logger)
    {
        _mediator = mediator;
        _tempFilesStorage = tempFilesStorage;
        _federatedDownload = federatedDownload;
        _usersClient = usersClient;
        _configuration = configuration;
        _metrics = metrics;
        _logger = logger;
    }

    [HttpPost("upload/{uploadId}")]
    [RequestSizeLimit(536_870_912)]
    [RequestFormLimits(MultipartBodyLengthLimit = 536_870_912)]
    public async Task<IActionResult> UploadFile([FromRoute] Guid uploadId, [FromForm] IFormFile? file)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest("Файл не выбран или пустой.");
        }

        using var command = new UploadFileCommand()
        {
            FileId = uploadId,
            FileStream = file.OpenReadStream(),
            FileName = file.FileName,
            FileSize = file.Length
        };

        try
        {
            var resultFileId = await _mediator.Send(command);
            _metrics.Increment("files_uploaded");
            _metrics.Add("upload_bytes_total", file.Length);
            _metrics.Add("file_traffic_bytes_total", file.Length);
            return Ok(new { fileId = resultFileId });
        }
        catch (FileAlreadyUploadedException ex)
        {
            _metrics.Increment("files_upload_errors");
            return BadRequest(ex.Message);
        }
        catch
        {
            _metrics.Increment("files_upload_errors");
            throw;
        }
    }

    /// <summary>
    /// Публичная ссылка на аватар remote-пользователя (этап 3.4). Прямой маршрут уместен
    /// именно для аватаров: локально они тоже публичны по оригинальному Guid.
    /// </summary>
    /// <remarks>
    /// Anti-open-proxy: пара (нода, file_id) обязана фигурировать в кеше remote-профилей,
    /// иначе маршрут проксировал бы произвольный файл с любой известной ноды.
    /// Неудача любой проверки — 404: существование файлов и нод не светим.
    /// </remarks>
    [HttpGet("download/fed/{serverName}/{fileId}")]
    public async Task<IActionResult> DownloadFederatedAvatar(
        [FromRoute] string serverName,
        [FromRoute] Guid fileId)
    {
        // Свои аватары качаются обычным /download — здесь их быть не должно.
        var ownServerName = _configuration["Federation:ServerName"];
        if (!string.IsNullOrEmpty(ownServerName)
            && string.Equals(serverName, ownServerName, StringComparison.OrdinalIgnoreCase))
        {
            return NotFound();
        }

        try
        {
            var reference = await _usersClient.CheckRemoteAvatarRefAsync(new CheckRemoteAvatarRefRequest
            {
                ServerName = serverName,
                FileId = fileId.ToString(),
            });

            if (!reference.Exists)
            {
                _metrics.Increment("fed_avatar_rejected");
                return NotFound();
            }

            await _federatedDownload.WriteAvatarToResponseAsync(
                serverName, fileId, GetFedAvatarMaxBytes(), HttpContext);

            _metrics.Increment("fed_avatars_served");
            return new EmptyResult();
        }
        catch (Exception ex)
        {
            // Недоступный/заблокированный origin здесь неотличим от несуществующей ссылки.
            // Классификация ошибок и placeholder-контракт — этап 3.5.
            _metrics.Increment("fed_avatar_errors");
            _logger.LogDebug(ex, "Не удалось отдать аватар {FileId} с ноды {Server}", fileId, serverName);
            return NotFound();
        }
    }

    private long GetFedAvatarMaxBytes()
    {
        var raw = _configuration["Files:FedAvatarMaxBytes"];
        return long.TryParse(raw, out var parsed) && parsed > 0 ? parsed : 20L * 1024 * 1024;
    }

    [HttpGet("download/{fileId}")]
    public async Task<IActionResult> DownloadFile([FromRoute] Guid fileId)
    {
        try
        {
            // Federated-вложение (этап 3.3): байты живут на чужой ноде и идут к клиенту
            // стримом. Хелпер File() тут не подходит — поток не seekable, а Range нужен
            // (перемотка видео), поэтому пишем в ответ вручную.
            var tempFile = await _tempFilesStorage.GetTempFile(fileId);
            if (tempFile?.OriginServer is { Length: > 0 })
            {
                await _federatedDownload.WriteToResponseAsync(tempFile, HttpContext);
                return new EmptyResult();
            }

            var command = new DownloadFileCommand()
            {
                FileId = fileId
            };

            var result = await _mediator.Send(command);
            _metrics.Increment("files_downloaded");
            if (result.FileStream.CanSeek)
            {
                _metrics.Add("download_bytes_total", result.FileStream.Length);
                _metrics.Add("file_traffic_bytes_total", result.FileStream.Length);
            }

            return File(result.FileStream, result.ContentType, result.FileName);
        }
        catch (FileNotUploadedException ex)
        {
            _metrics.Increment("files_download_errors");
            return NotFound(ex.Message);
        }
        catch (Exception ex)
        {
            _metrics.Increment("files_download_errors");
            _logger.LogError(ex, "Ошибка при скачивании файла {FileId}", fileId);
            return NotFound("Файл недоступен");
        }
    }
}
