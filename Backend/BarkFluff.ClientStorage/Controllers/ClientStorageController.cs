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
    private readonly LocalFileCache _cache;
    private readonly ILogger<ClientStorageController> _logger;

    public ClientStorageController(
        ClientStorageContext db,
        S3StorageService s3,
        LocalFileCache cache,
        ILogger<ClientStorageController> logger)
    {
        _db = db;
        _s3 = s3;
        _cache = cache;
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

    // --- Helpers ---

    private bool TryParseChannel(string channel, out ReleaseChannel releaseChannel)
    {
        switch (channel.ToLowerInvariant())
        {
            case "release": releaseChannel = ReleaseChannel.Release; return true;
            case "beta":    releaseChannel = ReleaseChannel.Beta;    return true;
            default:        releaseChannel = default;                return false;
        }
    }

    private static string GetDownloadPath(ClientType clientType, ReleaseChannel channel)
    {
        var slug = clientType switch
        {
            ClientType.Windows => "windows",
            ClientType.Kotlin  => "kotlin",
            ClientType.MacOS   => "macos",
            ClientType.iOS     => "ios",
            _                  => throw new ArgumentOutOfRangeException(nameof(clientType))
        };
        var ch = channel == ReleaseChannel.Release ? "release" : "beta";
        return $"/get/barkfluff{slug}/{ch}";
    }

    private string BuildPublicUrl(string path)
    {
        var baseUrl = HttpContext.RequestServices
            .GetRequiredService<IConfiguration>()["PUBLIC_BASE_URL"];

        if (!string.IsNullOrWhiteSpace(baseUrl))
            return baseUrl.TrimEnd('/') + path;

        return $"{Request.Scheme}://{Request.Host}{path}";
    }

    private Task<IActionResult> ParseChannelAndDownload(ClientType clientType, string channel)
    {
        if (!TryParseChannel(channel, out var rc))
            return Task.FromResult<IActionResult>(BadRequest(new { error = "Неизвестный канал. Допустимые значения: release, beta" }));
        return DownloadClient(clientType, rc);
    }

    private Task<IActionResult> ParseChannelAndGetVersion(ClientType clientType, string channel)
    {
        if (!TryParseChannel(channel, out var rc))
            return Task.FromResult<IActionResult>(BadRequest(new { error = "Неизвестный канал. Допустимые значения: release, beta" }));
        return GetVersion(clientType, rc);
    }

    private Task<IActionResult> ParseChannelAndUpload(IFormFile file, ClientType clientType, string channel)
    {
        if (!TryParseChannel(channel, out var rc))
            return Task.FromResult<IActionResult>(BadRequest(new { error = "Неизвестный канал. Допустимые значения: release, beta" }));
        return UploadClient(file, clientType, rc);
    }

    private Task<IActionResult> ParseChannelAndGetBitsUrl(ClientType clientType, string channel)
    {
        if (!TryParseChannel(channel, out var rc))
            return Task.FromResult<IActionResult>(BadRequest(new { error = "Неизвестный канал. Допустимые значения: release, beta" }));
        return GetBitsUrl(clientType, rc);
    }

    // --- Core actions ---

    /// <summary>
    /// Возвращает URL для BITS-задания + метаданные файла.
    /// URL указывает на сам ClientStorage (кешированный файл), S3 не фигурирует.
    /// </summary>
    private async Task<IActionResult> GetBitsUrl(ClientType clientType, ReleaseChannel releaseChannel)
    {
        var clientFile = await _db.ClientFiles
            .Where(f => f.ClientType == clientType && f.ReleaseChannel == releaseChannel)
            .OrderByDescending(f => f.UploadedAt)
            .FirstOrDefaultAsync();

        if (clientFile == null)
            return NotFound(new { error = "Файл клиента не найден" });

        var downloadUrl = BuildPublicUrl(GetDownloadPath(clientType, releaseChannel));

        return Ok(new
        {
            url        = downloadUrl,
            fileName   = clientFile.OriginalFileName,
            fileSize   = clientFile.FileSize,
            checksum   = clientFile.Checksum,
            version    = clientFile.Version,
            uploadedAt = clientFile.UploadedAt
        });
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
    /// Скачивание файла клиента.
    /// Приоритет: локальный кеш → стриминг из S3.
    /// Поддерживает Range-запросы в обоих случаях.
    /// </summary>
    private async Task<IActionResult> DownloadClient(ClientType clientType, ReleaseChannel releaseChannel)
    {
        var clientFile = await _db.ClientFiles
            .Where(f => f.ClientType == clientType && f.ReleaseChannel == releaseChannel)
            .OrderByDescending(f => f.UploadedAt)
            .FirstOrDefaultAsync();

        if (clientFile == null)
            return NotFound(new { error = "Файл клиента не найден" });

        Response.Headers.AcceptRanges = "bytes";

        // Кеш есть → отдаём с диска (быстро, без S3)
        var cachedPath = _cache.GetCachedFilePath(clientType, releaseChannel);
        if (cachedPath != null)
        {
            FileStream? fs = null;
            try
            {
                fs = new FileStream(cachedPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                return new FileStreamResult(fs, clientFile.ContentType)
                {
                    FileDownloadName      = clientFile.OriginalFileName,
                    EnableRangeProcessing = true
                };
                // FileStreamResult сам вызовет Dispose(fs) после отправки ответа
            }
            catch
            {
                fs?.Dispose();
                throw;
            }
        }

        // Кеша нет → стримим напрямую из S3
        _logger.LogWarning("Кеш отсутствует для {ClientType} {Channel}, стриминг из S3", clientType, releaseChannel);
        try
        {
            var rangeHeader = Request.Headers.Range.FirstOrDefault();
            var result      = await _s3.DownloadAsync(clientFile.S3Key, rangeHeader);
            return new FileStreamResult(result.Stream, clientFile.ContentType)
            {
                FileDownloadName      = clientFile.OriginalFileName,
                EnableRangeProcessing = true
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка стриминга из S3 для {S3Key}", clientFile.S3Key);
            return StatusCode(500, new { error = "Ошибка при скачивании файла" });
        }
    }

    /// <summary>
    /// Загрузка нового дистрибутива.
    /// Файл сохраняется в S3, затем асинхронно обновляется локальный кеш.
    /// </summary>
    private async Task<IActionResult> UploadClient(IFormFile? file, ClientType clientType, ReleaseChannel releaseChannel)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { error = "Файл не предоставлен" });

        var s3Key = Guid.NewGuid().ToString();
        string checksum;

        try
        {
            await using var sourceStream = file.OpenReadStream();

            using var incrementalHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[81920];
            int bytesRead;
            while ((bytesRead = await sourceStream.ReadAsync(buffer)) > 0)
                incrementalHash.AppendData(buffer, 0, bytesRead);
            checksum = Convert.ToHexStringLower(incrementalHash.GetHashAndReset());

            sourceStream.Position = 0;
            await _s3.UploadAsync(s3Key, sourceStream, file.ContentType ?? "application/octet-stream");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка загрузки файла {ClientType} в S3", clientType);
            return StatusCode(500, new { error = "Ошибка при загрузке файла" });
        }

        _logger.LogInformation("Файл {FileName} загружен в S3 как {S3Key}, checksum: {Checksum}",
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

        // Обновляем кеш асинхронно — не задерживаем ответ клиенту
        _ = UpdateCacheInBackgroundAsync(s3Key, clientType, releaseChannel);

        return Ok(new
        {
            fileName   = clientFile.OriginalFileName,
            s3Key      = clientFile.S3Key,
            checksum   = clientFile.Checksum,
            size       = clientFile.FileSize,
            uploadedAt = clientFile.UploadedAt
        });
    }

    private async Task UpdateCacheInBackgroundAsync(string s3Key, ClientType clientType, ReleaseChannel channel)
    {
        try
        {
            using var result = await _s3.DownloadAsync(s3Key);
            await _cache.UpdateAsync(clientType, channel, result.Stream);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка обновления кеша после загрузки: {ClientType} {Channel}", clientType, channel);
        }
    }
}
