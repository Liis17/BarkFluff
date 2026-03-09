using System.Collections.Concurrent;

using Amazon;
using Amazon.S3;
using Amazon.S3.Model;

using BarkFluff.Proto.Configuration;
using BarkFluff.Shared.Identity;

namespace Barkfluff.AdminPanel.Services;

public class S3BrowserService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ConcurrentDictionary<string, AmazonS3Client> _clients = new();
    private readonly ConcurrentDictionary<string, BucketConfig> _configs = new();
    private bool _initialized;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    public S3BrowserService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    private async Task EnsureInitializedAsync()
    {
        if (_initialized) return;

        await _initLock.WaitAsync();
        try
        {
            if (_initialized) return;
            await RefreshConfigAsync();
            _initialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    private async Task RefreshConfigAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var configClient = scope.ServiceProvider.GetRequiredService<ConfigurationApi.ConfigurationApiClient>();

        var response = await configClient.GetConfigurationAsync(new GetConfigurationRequest
        {
            ServiceId = (int)ServiceId.Files
        });

        var buckets = response.Configurations
            .Where(c => c.Section.StartsWith("S3Buckets:"))
            .GroupBy(c => c.Section.Replace("S3Buckets:", ""))
            .ToDictionary(
                g => g.Key,
                g => new BucketConfig
                {
                    ServiceUrl = g.FirstOrDefault(c => c.Key == "ServiceUrl")?.Value ?? "",
                    BucketName = g.FirstOrDefault(c => c.Key == "BucketName")?.Value ?? "",
                    AccessKey = g.FirstOrDefault(c => c.Key == "AccessKey")?.Value ?? "",
                    SecretKey = g.FirstOrDefault(c => c.Key == "SecretKey")?.Value ?? ""
                }
            );

        foreach (var (bucketId, config) in buckets)
        {
            _configs[bucketId] = config;
        }
    }

    private AmazonS3Client GetOrCreateClient(string bucketId)
    {
        return _clients.GetOrAdd(bucketId, id =>
        {
            if (!_configs.TryGetValue(id, out var config))
                throw new KeyNotFoundException($"Bucket '{id}' not found in configuration");

            var s3Config = new AmazonS3Config
            {
                ServiceURL = config.ServiceUrl,
                ForcePathStyle = true
            };

            return new AmazonS3Client(config.AccessKey, config.SecretKey, s3Config);
        });
    }

    public async Task<List<BucketInfo>> GetBucketNamesAsync()
    {
        await EnsureInitializedAsync();

        var displayNames = new Dictionary<string, string>
        {
            ["barkfluff-uploads"] = "Загрузки",
            ["profile-pictures"] = "Аватарки",
            ["message-documents"] = "Документы",
            ["message-videos"] = "Видео",
            ["message-images"] = "Изображения",
            ["message-audio"] = "Аудио",
            ["chat-pictures"] = "Фото чатов",
            ["badge-images"] = "Бейджи"
        };

        return _configs.Keys
            .OrderBy(k => k)
            .Select(k => new BucketInfo
            {
                Id = k,
                DisplayName = displayNames.TryGetValue(k, out var name) ? name : k
            })
            .ToList();
    }

    public async Task<ListObjectsResult> ListObjectsAsync(string bucketId, string? prefix, string? continuationToken, int maxKeys = 100)
    {
        await EnsureInitializedAsync();

        if (!_configs.TryGetValue(bucketId, out var config))
            throw new KeyNotFoundException($"Bucket '{bucketId}' not found");

        var client = GetOrCreateClient(bucketId);

        var request = new ListObjectsV2Request
        {
            BucketName = config.BucketName,
            Prefix = prefix ?? "",
            Delimiter = "/",
            MaxKeys = maxKeys,
            ContinuationToken = string.IsNullOrEmpty(continuationToken) ? null : continuationToken
        };

        var response = await client.ListObjectsV2Async(request);

        var objects = response.S3Objects
            .Where(o => o.Key != prefix) // exclude the folder itself
            .Select(o => new S3ObjectInfo
            {
                Key = o.Key,
                Name = GetFileName(o.Key, prefix),
                Size = o.Size,
                LastModified = o.LastModified,
                IsFolder = false
            })
            .ToList();

        var folders = response.CommonPrefixes
            .Select(p => new S3ObjectInfo
            {
                Key = p,
                Name = GetFolderName(p, prefix),
                Size = 0,
                LastModified = DateTime.MinValue,
                IsFolder = true
            })
            .ToList();

        return new ListObjectsResult
        {
            Objects = folders.Concat(objects).ToList(),
            NextContinuationToken = response.NextContinuationToken,
            IsTruncated = response.IsTruncated
        };
    }

    public async Task<string> GetPresignedUrlAsync(string bucketId, string key)
    {
        await EnsureInitializedAsync();

        if (!_configs.TryGetValue(bucketId, out var config))
            throw new KeyNotFoundException($"Bucket '{bucketId}' not found");

        var client = GetOrCreateClient(bucketId);

        var request = new GetPreSignedUrlRequest
        {
            BucketName = config.BucketName,
            Key = key,
            Expires = DateTime.UtcNow.AddMinutes(5),
            Verb = HttpVerb.GET
        };

        return await client.GetPreSignedURLAsync(request);
    }

    public void InvalidateCache(string? bucketId = null)
    {
        if (bucketId != null)
        {
            if (_clients.TryRemove(bucketId, out var client))
                client.Dispose();
            _configs.TryRemove(bucketId, out _);
        }
        else
        {
            foreach (var client in _clients.Values)
                client.Dispose();
            _clients.Clear();
            _configs.Clear();
        }

        _initialized = false;
    }

    private static string GetFileName(string key, string? prefix)
    {
        var relative = string.IsNullOrEmpty(prefix) ? key : key[prefix.Length..];
        return relative.TrimEnd('/');
    }

    private static string GetFolderName(string key, string? prefix)
    {
        var relative = string.IsNullOrEmpty(prefix) ? key : key[prefix.Length..];
        return relative.TrimEnd('/');
    }

    private class BucketConfig
    {
        public string ServiceUrl { get; set; } = "";
        public string BucketName { get; set; } = "";
        public string AccessKey { get; set; } = "";
        public string SecretKey { get; set; } = "";
    }
}

public class BucketInfo
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
}

public class S3ObjectInfo
{
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public long Size { get; set; }
    public DateTime LastModified { get; set; }
    public bool IsFolder { get; set; }
}

public class ListObjectsResult
{
    public List<S3ObjectInfo> Objects { get; set; } = new();
    public string? NextContinuationToken { get; set; }
    public bool IsTruncated { get; set; }
}
