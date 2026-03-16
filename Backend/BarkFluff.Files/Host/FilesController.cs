using BarkFluff.Files.Exceptions;
using BarkFluff.Files.Features.DownloadFile;
using BarkFluff.Files.Features.UploadFile;
using BarkFluff.GrpcServer.Metrics;

using MediatR;

using Microsoft.AspNetCore.Mvc;

namespace BarkFluff.Files.Host;

public class FilesController : Controller
{
    private readonly IMediator _mediator;
    private readonly MetricsCollector _metrics;

    public FilesController(IMediator mediator, MetricsCollector metrics)
    {
        _mediator = mediator;
        _metrics = metrics;
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
            return Ok(new { fileId = resultFileId });
        }
        catch (FileAlreadyUploadedException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpGet("download/{fileId}")]
    public async Task<IActionResult> DownloadFile([FromRoute] Guid fileId)
    {
        try
        {
            var command = new DownloadFileCommand()
            {
                FileId = fileId
            };

            var result = await _mediator.Send(command);

            return File(result.FileStream, result.ContentType, result.FileName);
        }
        catch (FileNotUploadedException ex)
        {
            return NotFound(ex.Message);
        }
        catch (Exception ex)
        {
            return NotFound($"Ошибка при скачивании файла: {ex.Message}");
        }
    }
}