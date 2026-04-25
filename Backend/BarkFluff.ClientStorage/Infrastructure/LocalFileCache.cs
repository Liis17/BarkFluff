using BarkFluff.ClientStorage.Domain;

namespace BarkFluff.ClientStorage.Infrastructure;

public class LocalFileCache
{
    private readonly string _cacheDir;
    private readonly ILogger<LocalFileCache> _logger;

    public LocalFileCache(IConfiguration configuration, ILogger<LocalFileCache> logger)
    {
        _cacheDir = configuration["CACHE_DIR"] ?? "/app/cache";
        Directory.CreateDirectory(_cacheDir);
        _logger = logger;
    }

    private string CachePath(ClientType clientType, ReleaseChannel channel)
    {
        var key = $"{clientType.ToString().ToLowerInvariant()}_{channel.ToString().ToLowerInvariant()}";
        return Path.Combine(_cacheDir, key);
    }

    public string? GetCachedFilePath(ClientType clientType, ReleaseChannel channel)
    {
        var path = CachePath(clientType, channel);
        return File.Exists(path) ? path : null;
    }

    public async Task UpdateAsync(ClientType clientType, ReleaseChannel channel, Stream source)
    {
        var path = CachePath(clientType, channel);
        var tmp  = path + ".tmp";
        try
        {
            await using var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None);
            await source.CopyToAsync(fs);
            File.Move(tmp, path, overwrite: true);
            _logger.LogInformation("Кеш обновлён: {ClientType} {Channel}", clientType, channel);
        }
        catch
        {
            try { File.Delete(tmp); } catch { /* ignore */ }
            throw;
        }
    }
}
