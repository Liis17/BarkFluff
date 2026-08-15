using BarkFluff.ClientStorage.Domain;
using BarkFluff.ClientStorage.Infrastructure;
using BarkFluff.ClientStorage.Persistence;
using BarkFluff.GrpcServer.Metrics;

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
    private readonly MetricsCollector _metrics;

    public ClientStorageController(
        ClientStorageContext db,
        S3StorageService s3,
        LocalFileCache cache,
        ILogger<ClientStorageController> logger,
        MetricsCollector metrics)
    {
        _db = db;
        _s3 = s3;
        _cache = cache;
        _logger = logger;
        _metrics = metrics;
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

    [HttpGet("/get/barkfluffwinui")]
    public Task<IActionResult> GetWinUI()
        => DownloadClient(ClientType.WinUI, ReleaseChannel.Release);

    [HttpGet("/get/barkfluffwinui/version")]
    public Task<IActionResult> GetWinUIVersion()
        => GetVersion(ClientType.WinUI, ReleaseChannel.Release);

    [HttpGet("/get/barkfluffwinui/{channel}")]
    public Task<IActionResult> GetWinUIChannel(string channel)
        => ParseChannelAndDownload(ClientType.WinUI, channel);

    [HttpGet("/get/barkfluffwinui/{channel}/version")]
    public Task<IActionResult> GetWinUIChannelVersion(string channel)
        => ParseChannelAndGetVersion(ClientType.WinUI, channel);

    [HttpPost("/set/barkfluffwindows")]
    [RequestSizeLimit(512L * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 512L * 1024 * 1024)]
    public Task<IActionResult> SetWindows(IFormFile file)
        => UploadClient(file, ClientType.Windows, ReleaseChannel.Release);

    [HttpPost("/set/barkfluffwindows/{channel}")]
    [RequestSizeLimit(512L * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 512L * 1024 * 1024)]
    public Task<IActionResult> SetWindowsChannel(IFormFile file, string channel)
        => ParseChannelAndUpload(file, ClientType.Windows, channel);

    [HttpPost("/set/barkfluffkotlin")]
    [RequestSizeLimit(512L * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 512L * 1024 * 1024)]
    public Task<IActionResult> SetKotlin(IFormFile file)
        => UploadClient(file, ClientType.Kotlin, ReleaseChannel.Release);

    [HttpPost("/set/barkfluffkotlin/{channel}")]
    [RequestSizeLimit(512L * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 512L * 1024 * 1024)]
    public Task<IActionResult> SetKotlinChannel(IFormFile file, string channel)
        => ParseChannelAndUpload(file, ClientType.Kotlin, channel);

    [HttpPost("/set/barkfluffmacos")]
    [RequestSizeLimit(512L * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 512L * 1024 * 1024)]
    public Task<IActionResult> SetMacOS(IFormFile file)
        => UploadClient(file, ClientType.MacOS, ReleaseChannel.Release);

    [HttpPost("/set/barkfluffmacos/{channel}")]
    [RequestSizeLimit(512L * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 512L * 1024 * 1024)]
    public Task<IActionResult> SetMacOSChannel(IFormFile file, string channel)
        => ParseChannelAndUpload(file, ClientType.MacOS, channel);

    [HttpPost("/set/barkfluffios")]
    [RequestSizeLimit(512L * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 512L * 1024 * 1024)]
    public Task<IActionResult> SetIOS(IFormFile file)
        => UploadClient(file, ClientType.iOS, ReleaseChannel.Release);

    [HttpPost("/set/barkfluffios/{channel}")]
    [RequestSizeLimit(512L * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 512L * 1024 * 1024)]
    public Task<IActionResult> SetIOSChannel(IFormFile file, string channel)
        => ParseChannelAndUpload(file, ClientType.iOS, channel);

    [HttpPost("/set/barkfluffwinui")]
    [RequestSizeLimit(512L * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 512L * 1024 * 1024)]
    public Task<IActionResult> SetWinUI(IFormFile file)
        => UploadClient(file, ClientType.WinUI, ReleaseChannel.Release);

    [HttpPost("/set/barkfluffwinui/{channel}")]
    [RequestSizeLimit(512L * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 512L * 1024 * 1024)]
    public Task<IActionResult> SetWinUIChannel(IFormFile file, string channel)
        => ParseChannelAndUpload(file, ClientType.WinUI, channel);

    // --- Helpers ---

    private bool TryParseChannel(string channel, out ReleaseChannel releaseChannel)
    {
        switch (channel.ToLowerInvariant())
        {
            case "release": releaseChannel = ReleaseChannel.Release; return true;
            case "beta":    releaseChannel = ReleaseChannel.Beta;    return true;
            case "dev":     releaseChannel = ReleaseChannel.Dev;     return true;
            case "nightly": releaseChannel = ReleaseChannel.Nightly; return true;
            default:         releaseChannel = default;                return false;
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
            ClientType.WinUI   => "winui",
            _                  => throw new ArgumentOutOfRangeException(nameof(clientType))
        };
        var ch = channel switch
        {
            ReleaseChannel.Release => "release",
            ReleaseChannel.Beta    => "beta",
            ReleaseChannel.Dev     => "dev",
            ReleaseChannel.Nightly => "nightly",
            _                      => throw new ArgumentOutOfRangeException(nameof(channel))
        };
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
            return Task.FromResult<IActionResult>(BadRequest(new { error = "Неизвестный канал. Допустимые значения: release, beta, dev, nightly" }));
        return DownloadClient(clientType, rc);
    }

    private Task<IActionResult> ParseChannelAndGetVersion(ClientType clientType, string channel)
    {
        if (!TryParseChannel(channel, out var rc))
            return Task.FromResult<IActionResult>(BadRequest(new { error = "Неизвестный канал. Допустимые значения: release, beta, dev, nightly" }));
        return GetVersion(clientType, rc);
    }

    private Task<IActionResult> ParseChannelAndUpload(IFormFile file, ClientType clientType, string channel)
    {
        if (!TryParseChannel(channel, out var rc))
            return Task.FromResult<IActionResult>(BadRequest(new { error = "Неизвестный канал. Допустимые значения: release, beta, dev, nightly" }));
        return UploadClient(file, clientType, rc);
    }

    private Task<IActionResult> ParseChannelAndGetBitsUrl(ClientType clientType, string channel)
    {
        if (!TryParseChannel(channel, out var rc))
            return Task.FromResult<IActionResult>(BadRequest(new { error = "Неизвестный канал. Допустимые значения: release, beta, dev, nightly" }));
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
            _metrics.Increment("client_cache_hits");
            FileStream? fs = null;
            try
            {
                fs = new FileStream(cachedPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                Response.OnCompleted(() =>
                {
                    _metrics.Increment("client_releases_downloaded");
                    if (Response.ContentLength is long contentLength)
                        _metrics.Add("client_storage_download_bytes", contentLength);
                    return Task.CompletedTask;
                });
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
        _metrics.Increment("client_cache_misses");
        _logger.LogWarning("Кеш отсутствует для {ClientType} {Channel}, стриминг из S3", clientType, releaseChannel);
        S3DownloadResult? result = null;
        try
        {
            result = await _s3.DownloadAsync(clientFile.S3Key, Request.Headers.Range.FirstOrDefault());
        }
        catch (Exception ex)
        {
            _metrics.Increment("client_storage_errors");
            _logger.LogError(ex, "Ошибка получения объекта из S3 для {S3Key}", clientFile.S3Key);
            return StatusCode(500, new { error = "Ошибка при скачивании файла" });
        }

        using (result)
        {
            Response.ContentType   = clientFile.ContentType;
            Response.ContentLength = result.ContentLength;
            Response.Headers["Content-Disposition"] = $"attachment; filename=\"{clientFile.OriginalFileName}\"";

            if (!string.IsNullOrEmpty(result.ContentRange))
            {
                Response.StatusCode              = StatusCodes.Status206PartialContent;
                Response.Headers["Content-Range"] = result.ContentRange;
            }

            await result.Stream.CopyToAsync(Response.Body);
            _metrics.Increment("client_releases_downloaded");
            _metrics.Add("client_storage_download_bytes", result.ContentLength);
            return new EmptyResult();
        }
    }

    /// <summary>
    /// Загрузка нового дистрибутива.
    /// Тело запроса сохраняется в локальный temp-файл с попутным подсчётом SHA-256 (один проход по сети),
    /// затем temp-файл стримится в S3 как обычный seekable FileStream — без танцев с
    /// non-seekable стримом и DisablePayloadSigning.
    /// После ответа клиенту локальный кеш обновляется в фоне.
    /// </summary>
    private async Task<IActionResult> UploadClient(IFormFile? file, ClientType clientType, ReleaseChannel releaseChannel)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { error = "Файл не предоставлен" });

        var s3Key   = Guid.NewGuid().ToString();
        var tempDir = Path.Combine(Path.GetTempPath(), "barkfluff-clientstorage-uploads");
        Directory.CreateDirectory(tempDir);
        var tempPath = Path.Combine(tempDir, s3Key);
        string checksum;

        bool s3Succeeded = false;
        try
        {
            // 1) принимаем файл с попутным SHA-256
            using (var incrementalHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                await using var sourceStream = file.OpenReadStream();
                await using var tempStream   = new FileStream(
                    tempPath, FileMode.Create, FileAccess.Write, FileShare.None,
                    bufferSize: 81920, useAsync: true);

                var buffer = new byte[81920];
                int read;
                while ((read = await sourceStream.ReadAsync(buffer)) > 0)
                {
                    incrementalHash.AppendData(buffer, 0, read);
                    await tempStream.WriteAsync(buffer.AsMemory(0, read));
                }
                checksum = Convert.ToHexStringLower(incrementalHash.GetHashAndReset());
            }

            // 2) грузим temp-файл в S3 через TransferUtility (multipart)
            await _s3.UploadAsync(
                tempPath,
                s3Key,
                file.ContentType ?? "application/octet-stream");
            s3Succeeded = true;
        }
        catch (Amazon.S3.AmazonS3Exception s3ex)
        {
            _metrics.Increment("client_storage_errors");
            _logger.LogError(s3ex,
                "S3 error: Status={Status} Code={Code} RequestId={ReqId} Message={Msg}",
                s3ex.StatusCode, s3ex.ErrorCode, s3ex.RequestId, s3ex.Message);
            return StatusCode(500, new { error = $"S3: {s3ex.ErrorCode ?? "unknown"} ({s3ex.StatusCode})" });
        }
        catch (Exception ex)
        {
            _metrics.Increment("client_storage_errors");
            _logger.LogError(ex, "Ошибка загрузки файла {ClientType} в S3", clientType);
            return StatusCode(500, new { error = "Ошибка при загрузке файла" });
        }
        finally
        {
            // На успешном пути temp отдаём фоновой задаче — она сама его удалит после прогрева кэша
            if (!s3Succeeded)
            {
                try { if (System.IO.File.Exists(tempPath)) System.IO.File.Delete(tempPath); }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Не удалось удалить temp-файл {Path}", tempPath);
                }
            }
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
        _metrics.Increment("client_releases_uploaded");
        _metrics.Add("client_storage_upload_bytes", file.Length);

        // Обновляем кеш асинхронно — не задерживаем ответ клиенту
        _ = UpdateCacheInBackgroundAsync(s3Key, clientType, releaseChannel, tempPath);

        return Ok(new
        {
            fileName   = clientFile.OriginalFileName,
            s3Key      = clientFile.S3Key,
            checksum   = clientFile.Checksum,
            size       = clientFile.FileSize,
            uploadedAt = clientFile.UploadedAt
        });
    }

    private async Task UpdateCacheInBackgroundAsync(string s3Key, ClientType clientType, ReleaseChannel channel,
        string? tempPath = null)
    {
        if (tempPath != null && System.IO.File.Exists(tempPath))
        {
            try
            {
                await _cache.UpdateFromFileAsync(clientType, channel, tempPath);
                _logger.LogInformation("Кеш обновлён из temp-файла: {ClientType} {Channel}", clientType, channel);
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Не удалось обновить кеш из temp-файла, скачиваем из S3");
            }
            finally
            {
                try { System.IO.File.Delete(tempPath); } catch { }
            }
        }

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
