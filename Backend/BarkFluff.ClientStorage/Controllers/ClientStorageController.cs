using BarkFluff.ClientStorage.Domain;
using BarkFluff.ClientStorage.Infrastructure;
using BarkFluff.ClientStorage.Persistence;

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using System.Security.Cryptography;

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
    public Task<IActionResult> GetWindows()
        => DownloadClient(ClientType.Windows, ReleaseChannel.Release);

    [HttpGet("/get/barkfluffwindows/version")]
    public Task<IActionResult> GetWindowsVersion()
        => GetVersion(ClientType.Windows, ReleaseChannel.Release);

    [HttpGet("/get/barkfluffwindows/{channel}")]
    public Task<IActionResult> GetWindowsChannel(string channel)
        => ParseChannelAndDownload(ClientType.Windows, channel);

    [HttpGet("/get/barkfluffwindows/{channel}/version")]
    public Task<IActionResult> GetWindowsChannelVersion(string channel)
        => ParseChannelAndGetVersion(ClientType.Windows, channel);

    [HttpGet("/get/barkfluffkotlin")]
    public Task<IActionResult> GetKotlin()
        => DownloadClient(ClientType.Kotlin, ReleaseChannel.Release);

    [HttpGet("/get/barkfluffkotlin/version")]
    public Task<IActionResult> GetKotlinVersion()
        => GetVersion(ClientType.Kotlin, ReleaseChannel.Release);

    [HttpGet("/get/barkfluffkotlin/{channel}")]
    public Task<IActionResult> GetKotlinChannel(string channel)
        => ParseChannelAndDownload(ClientType.Kotlin, channel);

    [HttpGet("/get/barkfluffkotlin/{channel}/version")]
    public Task<IActionResult> GetKotlinChannelVersion(string channel)
        => ParseChannelAndGetVersion(ClientType.Kotlin, channel);

    [HttpGet("/get/barkfluffmacos")]
    public Task<IActionResult> GetMacOS()
        => DownloadClient(ClientType.MacOS, ReleaseChannel.Release);

    [HttpGet("/get/barkfluffmacos/version")]
    public Task<IActionResult> GetMacOSVersion()
        => GetVersion(ClientType.MacOS, ReleaseChannel.Release);

    [HttpGet("/get/barkfluffmacos/{channel}")]
    public Task<IActionResult> GetMacOSChannel(string channel)
        => ParseChannelAndDownload(ClientType.MacOS, channel);

    [HttpGet("/get/barkfluffmacos/{channel}/version")]
    public Task<IActionResult> GetMacOSChannelVersion(string channel)
        => ParseChannelAndGetVersion(ClientType.MacOS, channel);

    [HttpGet("/get/barkfluffios")]
    public Task<IActionResult> GetIOS()
        => DownloadClient(ClientType.iOS, ReleaseChannel.Release);

    [HttpGet("/get/barkfluffios/version")]
    public Task<IActionResult> GetIOSVersion()
        => GetVersion(ClientType.iOS, ReleaseChannel.Release);

    [HttpGet("/get/barkfluffios/{channel}")]
    public Task<IActionResult> GetIOSChannel(string channel)
        => ParseChannelAndDownload(ClientType.iOS, channel);

    [HttpGet("/get/barkfluffios/{channel}/version")]
    public Task<IActionResult> GetIOSChannelVersion(string channel)
        => ParseChannelAndGetVersion(ClientType.iOS, channel);

    [HttpPost("/set/barkfluffwindows")]
    [RequestSizeLimit(512 * 1024 * 1024)]
    public Task<IActionResult> SetWindows(IFormFile file)
        => UploadClient(file, ClientType.Windows, ReleaseChannel.Release);

    [HttpPost("/set/barkfluffwindows/{channel}")]
    [RequestSizeLimit(512 * 1024 * 1024)]
    public Task<IActionResult> SetWindowsChannel(IFormFile file, string channel)
        => ParseChannelAndUpload(file, ClientType.Windows, channel);

    [HttpPost("/set/barkfluffkotlin")]
    [RequestSizeLimit(512 * 1024 * 1024)]
    public Task<IActionResult> SetKotlin(IFormFile file)
        => UploadClient(file, ClientType.Kotlin, ReleaseChannel.Release);

    [HttpPost("/set/barkfluffkotlin/{channel}")]
    [RequestSizeLimit(512 * 1024 * 1024)]
    public Task<IActionResult> SetKotlinChannel(IFormFile file, string channel)
        => ParseChannelAndUpload(file, ClientType.Kotlin, channel);

    [HttpPost("/set/barkfluffmacos")]
    [RequestSizeLimit(512 * 1024 * 1024)]
    public Task<IActionResult> SetMacOS(IFormFile file)
        => UploadClient(file, ClientType.MacOS, ReleaseChannel.Release);

    [HttpPost("/set/barkfluffmacos/{channel}")]
    [RequestSizeLimit(512 * 1024 * 1024)]
    public Task<IActionResult> SetMacOSChannel(IFormFile file, string channel)
        => ParseChannelAndUpload(file, ClientType.MacOS, channel);

    [HttpPost("/set/barkfluffios")]
    [RequestSizeLimit(512 * 1024 * 1024)]
    public Task<IActionResult> SetIOS(IFormFile file)
        => UploadClient(file, ClientType.iOS, ReleaseChannel.Release);

    [HttpPost("/set/barkfluffios/{channel}")]
    [RequestSizeLimit(512 * 1024 * 1024)]
    public Task<IActionResult> SetIOSChannel(IFormFile file, string channel)
        => ParseChannelAndUpload(file, ClientType.iOS, channel);

    private bool TryParseChannel(string channel, out ReleaseChannel releaseChannel)
    {
        switch (channel.ToLowerInvariant())
        {
            case "release":
                releaseChannel = ReleaseChannel.Release;
                return true;
            case "beta":
                releaseChannel = ReleaseChannel.Beta;
                return true;
            default:
                releaseChannel = default;
                return false;
        }
    }

    private async Task<IActionResult> ParseChannelAndDownload(ClientType clientType, string channel)
    {
        if (!TryParseChannel(channel, out var releaseChannel))
            return BadRequest(new { error = "Неизвестный канал. Допустимые значения: release, beta" });

        return await DownloadClient(clientType, releaseChannel);
    }

    private async Task<IActionResult> ParseChannelAndGetVersion(ClientType clientType, string channel)
    {
        if (!TryParseChannel(channel, out var releaseChannel))
            return BadRequest(new { error = "Неизвестный канал. Допустимые значения: release, beta" });

        return await GetVersion(clientType, releaseChannel);
    }

    private async Task<IActionResult> ParseChannelAndUpload(IFormFile file, ClientType clientType, string channel)
    {
        if (!TryParseChannel(channel, out var releaseChannel))
            return BadRequest(new { error = "Неизвестный канал. Допустимые значения: release, beta" });

        return await UploadClient(file, clientType, releaseChannel);
    }

    private async Task<IActionResult> GetVersion(ClientType clientType, ReleaseChannel releaseChannel)
    {
        var clientFile = await _db.ClientFiles
            .Where(f => f.ClientType == clientType && f.ReleaseChannel == releaseChannel)
            .OrderByDescending(f => f.UploadedAt)
            .FirstOrDefaultAsync();

        if (clientFile == null)
            return NotFound(new { error = "Файл клиента не найден" });

        return Ok(new
        {
            version = clientFile.Version,
            uploadedAt = clientFile.UploadedAt,
            fileName = clientFile.OriginalFileName
        });
    }

    private async Task<IActionResult> DownloadClient(ClientType clientType, ReleaseChannel releaseChannel)
    {
        var clientFile = await _db.ClientFiles
            .Where(f => f.ClientType == clientType && f.ReleaseChannel == releaseChannel)
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

    private async Task<IActionResult> UploadClient(IFormFile? file, ClientType clientType, ReleaseChannel releaseChannel)
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

            // Считываем версию из заголовка (опционально)
            var version = Request.Headers["X-App-Version"].FirstOrDefault();

            // Сохраняем запись в БД
            var clientFile = new ClientFile
            {
                ClientType = clientType,
                ReleaseChannel = releaseChannel,
                OriginalFileName = file.FileName,
                S3Key = s3Key,
                ContentType = file.ContentType ?? "application/octet-stream",
                FileSize = file.Length,
                Checksum = checksum,
                UploadedAt = DateTime.UtcNow,
                Version = version
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
