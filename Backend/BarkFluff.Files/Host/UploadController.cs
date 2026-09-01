using BarkFluff.Files.Features.UploadFile;
using BarkFluff.Files.Persistence;
using BarkFluff.GrpcServer.Metrics;

using MediatR;

using Microsoft.AspNetCore.Mvc;

namespace BarkFluff.Files.Host;

public sealed class UploadController : Controller
{
    private static readonly TimeSpan ProcessingLease = TimeSpan.FromMinutes(35);

    private readonly IMediator _mediator;
    private readonly UploadedFilesStorage _filesStorage;
    private readonly MetricsCollector _metrics;

    public UploadController(
        IMediator mediator,
        UploadedFilesStorage filesStorage,
        MetricsCollector metrics)
    {
        _mediator = mediator;
        _filesStorage = filesStorage;
        _metrics = metrics;
    }

    [HttpPost("upload/{uploadId}")]
    [RequestSizeLimit(536_870_912)]
    [RequestFormLimits(MultipartBodyLengthLimit = 536_870_912)]
    public async Task<IActionResult> UploadFile([FromRoute] Guid uploadId, [FromForm] IFormFile? file)
    {
        if (file == null || file.Length == 0)
            return BadRequest("Файл не выбран или пустой.");

        var cancellationToken = HttpContext.RequestAborted;
        var claim = await _filesStorage.ClaimUploadAsync(
            uploadId, DateTime.UtcNow, ProcessingLease, cancellationToken);

        if (claim.Outcome == UploadClaimOutcome.NotFound)
            return NotFound();
        if (claim.Outcome == UploadClaimOutcome.Completed)
            return Ok(new UploadFileResponse(claim.ResultFileId!.Value));
        if (claim.Outcome == UploadClaimOutcome.Processing)
        {
            Response.Headers.RetryAfter = claim.RetryAfterSeconds.ToString();
            return StatusCode(
                StatusCodes.Status409Conflict,
                new UploadStatusResponse(uploadId, "processing", claim.RetryAfterSeconds));
        }

        var leaseToken = claim.LeaseToken!.Value;
        using var command = new UploadFileCommand
        {
            FileId = uploadId,
            LeaseToken = leaseToken,
            FileStream = file.OpenReadStream(),
            FileName = file.FileName,
            FileSize = file.Length,
        };

        try
        {
            var result = await _mediator.Send(command, cancellationToken);
            var resultFileId = Guid.Parse(result);
            await _filesStorage.CompleteUploadAsync(
                uploadId, leaseToken, resultFileId, cancellationToken);

            _metrics.Increment("files_uploaded");
            _metrics.Add("upload_bytes_total", file.Length);
            _metrics.Add("file_traffic_bytes_total", file.Length);
            return Ok(new UploadFileResponse(resultFileId));
        }
        catch
        {
            _metrics.Increment("files_upload_errors");
            await _filesStorage.ReleaseUploadAsync(uploadId, leaseToken, CancellationToken.None);
            throw;
        }
    }

    [HttpGet("upload/{uploadId}/status")]
    public async Task<IActionResult> GetUploadStatus([FromRoute] Guid uploadId)
    {
        var status = await _filesStorage.GetUploadStatusAsync(
            uploadId, DateTime.UtcNow, HttpContext.RequestAborted);
        if (status is null)
            return NotFound();

        return Ok(new UploadStatusResponse(
            status.ResultFileId ?? uploadId,
            status.State,
            status.RetryAfterSeconds));
    }
}

public sealed record UploadFileResponse(Guid FileId);

public sealed record UploadStatusResponse(Guid FileId, string State, int RetryAfterSeconds);
