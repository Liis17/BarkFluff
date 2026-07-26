using BarkFluff.Files.Exceptions;
using BarkFluff.Files.Features.DownloadFile;
using BarkFluff.Files.Features.UploadFile;
using BarkFluff.Files.Persistence;
using BarkFluff.GrpcServer.Metrics;

using MediatR;

using Microsoft.AspNetCore.Mvc;

namespace BarkFluff.Files.Host;

public class FilesController : Controller
{
    private readonly IMediator _mediator;
    private readonly TempFilesStorage _tempFilesStorage;
    private readonly FederatedDownloadService _federatedDownload;
    private readonly MetricsCollector _metrics;
    private readonly ILogger<FilesController> _logger;

    public FilesController(
        IMediator mediator,
        TempFilesStorage tempFilesStorage,
        FederatedDownloadService federatedDownload,
        MetricsCollector metrics,
        ILogger<FilesController> logger)
    {
        _mediator = mediator;
        _tempFilesStorage = tempFilesStorage;
        _federatedDownload = federatedDownload;
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
