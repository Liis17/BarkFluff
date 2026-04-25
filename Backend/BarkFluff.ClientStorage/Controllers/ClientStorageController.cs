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

    /// <summary>Возвращает presigned URL + метаданные для создания BITS-задания.</summary>
    [HttpGet("/get/barkfluffwindows/bitsurl")]
    public Task<IActionResult> GetWindowsBitsUrl()
        => GetBitsUrl(ClientType.Windows, ReleaseChannel.Release);

    [HttpGet("/get/barkfluffwindows/{channel}/bitsurl")]
    public Task<IActionResult> GetWindowsChannelBitsUrl(string channel)
        => ParseChannelAndGetBitsUrl(ClientType.Windows, channel);

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

    private async Task<IActionResult> ParseChannelAndGetBitsUrl(ClientType clientType, string channel)
    {
        if (!TryParseChannel(channel, out var releaseChannel))
            return BadRequest(new { error = "Неизвестный канал. Допустимые значения: release, beta" });
        return await GetBitsUrl(clientType, releaseChannel);
    }

    /// <summary>
    /// Возвращает presigned URL + метаданные для создания BITS-задания на Windows-клиенте.
    /// Ответ содержит URL, размер файла и SHA-256 checksum для проверки целостности.
    /// </summary>
    private async Task<IActionResult> GetBitsUrl(ClientType clientType, ReleaseChannel releaseChannel)
    {
        var clientFile = await _db.ClientFiles
            .Where(f => f.ClientType == clientType && f.ReleaseChannel == releaseChannel)
            .OrderByDescending(f => f.UploadedAt)
            .FirstOrDefaultAsync();

        if (clientFile == null)
            return NotFound(new { error = "Файл клиента не найден" });

        try
        {
            var presignedUrl = _s3.GeneratePresignedUrl(clientFile.S3Key, clientFile.OriginalFileName);

            // Ответ содержит всё для Windows BITS:
            //   url       → передать BitsJob.AddFile(url, localPath)
            //   fileSize  → необязательно, но удобно для прогресса
            //   checksum  → SHA-256 hex, BitsJob.SetHashAndAlgorithm(checksum, BG_AUTH_SCHEME.Sha256)
            return Ok(new
            {
                url        = presignedUrl,
                fileName   = clientFile.OriginalFileName,
                fileSize   = clientFile.FileSize,
                checksum   = clientFile.Checksum,
                version    = clientFile.Version,
                uploadedAt = clientFile.UploadedAt
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка генерации presigned URL для {S3Key}", clientFile.S3Key);
            return StatusCode(500, new { error = "Ошибка генерации URL" });
        }
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
            version    = clientFile.Version,
            uploadedAt = clientFile.UploadedAt,
            fileName   = clientFile.OriginalFileName
        });
    }

    /// <summary>
    /// Стриминговая отдача файла с поддержкой Range-запросов (возобновляемые загрузки, BITS).
    /// Данные идут напрямую из S3 → клиент, без буферизации в памяти сервера.
    /// </summary>
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
            var rangeHeader = Request.Headers.Range.FirstOrDefault();
            var result = await _s3.DownloadAsync(clientFile.S3Key, rangeHeader);

            // Сообщаем клиенту, что поддерживаем Range — обязательно для BITS
            Response.Headers.AcceptRanges = "bytes";

            // FileStreamResult сам пишет поток в response и вызовет Dispose по завершении
            return new FileStreamResult(result.Stream, clientFile.ContentType)
            {
                FileDownloadName      = clientFile.OriginalFileName,
                EnableRangeProcessing = true
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при скачивании файла {S3Key} из S3", clientFile.S3Key);
            return StatusCode(500, new { error = "Ошибка при скачивании файла" });
        }
    }

    /// <summary>
    /// Загрузка файла в S3.
    /// SHA-256 вычисляется за один проход через HashingReadStream —
    /// без создания дополнительного временного файла на диске.
    /// </summary>
    private async Task<IActionResult> UploadClient(IFormFile? file, ClientType clientType, ReleaseChannel releaseChannel)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { error = "Файл не предоставлен" });

        var s3Key = Guid.NewGuid().ToString();
        string checksum;

        try
        {
            // Один проход: HashingReadStream считает хэш во время чтения данных для S3
            await using var sourceStream = file.OpenReadStream();
            using var incrementalHash   = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using var hashStream  = new HashingReadStream(sourceStream, incrementalHash);

            await _s3.UploadAsync(s3Key, hashStream, file.ContentType ?? "application/octet-stream");

            checksum = Convert.ToHexStringLower(incrementalHash.GetHashAndReset());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при загрузке файла клиента {ClientType} в S3", clientType);
            return StatusCode(500, new { error = "Ошибка при загрузке файла" });
        }

        _logger.LogInformation(
            "Файл {FileName} загружен в S3 как {S3Key}, checksum: {Checksum}",
            file.FileName, s3Key, checksum);

        var version = Request.Headers["X-App-Version"].FirstOrDefault();

        var clientFile = new ClientFile
        {
            ClientType       = clientType,
            ReleaseChannel   = releaseChannel,
            OriginalFileName = file.FileName,
            S3Key            = s3Key,
            ContentType      = file.ContentType ?? "application/octet-stream",
            FileSize         = file.Length,
            Checksum         = checksum,
            UploadedAt       = DateTime.UtcNow,
            Version          = version
        };

        _db.ClientFiles.Add(clientFile);
        await _db.SaveChangesAsync();

        return Ok(new
        {
            fileName   = clientFile.OriginalFileName,
            s3Key      = clientFile.S3Key,
            checksum   = clientFile.Checksum,
            size       = clientFile.FileSize,
            uploadedAt = clientFile.UploadedAt
        });
    }
}

