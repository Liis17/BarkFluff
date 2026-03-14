using System.Security.Cryptography;

using BarkFluff.ClientStorage.Domain;
using BarkFluff.ClientStorage.Infrastructure;
using BarkFluff.ClientStorage.Persistence;

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BarkFluff.ClientStorage.Controllers;

[ApiController]
public class ClientStorageController : ControllerBase
{
    private readonly ClientStorageContext _db;
    private readonly S3StorageService _s3;
    private readonly ILogger<ClientStorageController> _logger;

    public ClientStorageController(
        ClientStorageContext db,
        S3StorageService s3,
        ILogger<ClientStorageController> logger)
    {
        _db = db;
        _s3 = s3;
        _logger = logger;
    }

    [HttpGet("/get/barkfluffwindows")]
    public async Task<IActionResult> GetWindows()
    {
        return await DownloadClient(ClientType.Windows);
    }

    [HttpGet("/get/barkfluffkotlin")]
    public async Task<IActionResult> GetKotlin()
    {
        return await DownloadClient(ClientType.Kotlin);
    }

    [HttpPost("/set/barkfluffwindows")]
    [RequestSizeLimit(512 * 1024 * 1024)]
    public async Task<IActionResult> SetWindows(IFormFile file)
    {
        return await UploadClient(file, ClientType.Windows);
    }

    [HttpPost("/set/barkfluffkotlin")]
    [RequestSizeLimit(512 * 1024 * 1024)]
    public async Task<IActionResult> SetKotlin(IFormFile file)
    {
        return await UploadClient(file, ClientType.Kotlin);
    }

    private async Task<IActionResult> DownloadClient(ClientType clientType)
    {
        var clientFile = await _db.ClientFiles
            .Where(f => f.ClientType == clientType)
            .OrderByDescending(f => f.UploadedAt)
            .FirstOrDefaultAsync();

        if (clientFile == null)
            return NotFound(new { error = "Файл клиента не найден" });

        try
        {
            var (stream, _) = await _s3.DownloadAsync(clientFile.S3Key);
            return File(stream, clientFile.ContentType, clientFile.OriginalFileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при скачивании файла {S3Key} из S3", clientFile.S3Key);
            return StatusCode(500, new { error = "Ошибка при скачивании файла" });
        }
    }

    private async Task<IActionResult> UploadClient(IFormFile? file, ClientType clientType)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { error = "Файл не предоставлен" });

        var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

        try
        {
            // Сохраняем во временный файл
            await using (var tempStream = new FileStream(tempPath, FileMode.Create))
            {
                await file.CopyToAsync(tempStream);
            }

            // Проверяем целостность — вычисляем контрольную сумму
            string checksum;
            await using (var hashStream = new FileStream(tempPath, FileMode.Open, FileAccess.Read))
            {
                using var sha256 = SHA256.Create();
                var hashBytes = await sha256.ComputeHashAsync(hashStream);
                checksum = Convert.ToHexStringLower(hashBytes);
            }

            // Загружаем в S3 с GUID-именем
            var s3Key = Guid.NewGuid().ToString();
            await using (var uploadStream = new FileStream(tempPath, FileMode.Open, FileAccess.Read))
            {
                await _s3.UploadAsync(s3Key, uploadStream, file.ContentType ?? "application/octet-stream");
            }

            _logger.LogInformation(
                "Файл {FileName} загружен в S3 как {S3Key}, checksum: {Checksum}",
                file.FileName, s3Key, checksum);

            // Сохраняем запись в БД
            var clientFile = new ClientFile
            {
                ClientType = clientType,
                OriginalFileName = file.FileName,
                S3Key = s3Key,
                ContentType = file.ContentType ?? "application/octet-stream",
                FileSize = file.Length,
                Checksum = checksum,
                UploadedAt = DateTime.UtcNow
            };

            _db.ClientFiles.Add(clientFile);
            await _db.SaveChangesAsync();

            return Ok(new
            {
                fileName = clientFile.OriginalFileName,
                s3Key = clientFile.S3Key,
                checksum = clientFile.Checksum,
                size = clientFile.FileSize,
                uploadedAt = clientFile.UploadedAt
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при загрузке файла клиента {ClientType}", clientType);
            return StatusCode(500, new { error = "Ошибка при загрузке файла" });
        }
        finally
        {
            // Удаляем временный файл
            if (System.IO.File.Exists(tempPath))
                System.IO.File.Delete(tempPath);
        }
    }
}
