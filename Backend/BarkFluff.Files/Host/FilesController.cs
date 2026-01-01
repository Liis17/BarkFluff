using BarkFluff.Files.Exceptions;
using BarkFluff.Files.Features.DownloadFile;
using BarkFluff.Files.Features.UploadFile;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace BarkFluff.Files.Host;

public class FilesController : Controller
{
    private readonly IMediator _mediator;

    public FilesController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpPost("upload/{uploadId}")]
    [RequestSizeLimit(500_000_000)]
    [RequestFormLimits(MultipartBodyLengthLimit = 500_000_000)]
    public async Task<IActionResult> UploadFile([FromRoute]Guid uploadId, [FromForm] IFormFile? file)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest("Файл не выбран или пустой.");
        }
        
        var memoryStream = new MemoryStream();
        await file.CopyToAsync(memoryStream);
        memoryStream.Position = 0; // Сбрасываем позицию в начало
        
        var command = new UploadFileCommand()
        {
            FileId = uploadId,
            FileStream = memoryStream,
            FileName = file.FileName
        };
        
        try 
        {
            await _mediator.Send(command);
            return Ok(new { fileId = uploadId });
        }
        catch (FileAlreadyUploadedException ex)
        {
            await memoryStream.DisposeAsync();
            return BadRequest(ex.Message);
        }
        catch
        {
            await memoryStream.DisposeAsync();
            throw;
        }
    }
    
    [HttpGet("download/{fileId}")]
    public async Task<IActionResult> DownloadFile([FromRoute]Guid fileId)
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