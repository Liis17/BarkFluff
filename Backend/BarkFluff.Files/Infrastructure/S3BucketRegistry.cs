using Amazon.Runtime;
using Amazon.S3;

using BarkFluff.Files.Configurations;
using BarkFluff.Files.Domain;

namespace BarkFluff.Files.Infrastructure;

/// <summary>
/// Реестр S3 бакетов. Управляет конфигурацией бакетов и клиентами S3 для каждого бакета.
/// Каждый бакет может находиться на отдельном S3-совместимом хранилище со своими учетными данными.
/// </summary>
public class S3BucketRegistry : IDisposable
{
    /// <summary>
    /// Соответствие типов файлов идентификаторам бакетов (логическое имя бакета в конфигурации)
    /// </summary>
    private static readonly Dictionary<UploadFileType, string> BucketIdMap = new()
    {
        { UploadFileType.Unknown, "barkfluff-uploads" },
        { UploadFileType.UserAvatar, "profile-pictures" },
        { UploadFileType.MessageAttachmentDocument, "message-documents" },
        { UploadFileType.MessageAttachmentVideo, "message-videos" },
        { UploadFileType.MessageAttachmentGif, "message-videos" },
        { UploadFileType.MessageAttachmentImage, "message-images" },
        { UploadFileType.ChatPicture, "chat-pictures" },
    };

    /// <summary>
    /// Конфигурация бакетов по идентификатору бакета
    /// </summary>
    private readonly Dictionary<string, BucketS3Options> _bucketConfigs;

    /// <summary>
    /// S3 клиенты по имени бакета (реальное имя из конфигурации)
    /// </summary>
    private readonly Dictionary<string, IAmazonS3> _clientsByBucketName;

    /// <summary>
    /// Кэш уникальных S3 клиентов для корректной очистки ресурсов
    /// </summary>
    private readonly List<IAmazonS3> _uniqueClients;

    public S3BucketRegistry(IConfiguration configuration)
    {
        _bucketConfigs = new Dictionary<string, BucketS3Options>();
        _clientsByBucketName = new Dictionary<string, IAmazonS3>();

        var s3BucketsSection = configuration.GetSection("S3Buckets");

        foreach (var bucketSection in s3BucketsSection.GetChildren())
        {
            var options = new BucketS3Options();
            bucketSection.Bind(options);

            // Если BucketName не задан явно, используем идентификатор из конфигурации
            if (string.IsNullOrWhiteSpace(options.BucketName))
            {
                options.BucketName = bucketSection.Key;
            }

            _bucketConfigs[bucketSection.Key] = options;
        }

        // Создаем S3 клиенты с кэшированием по уникальным параметрам подключения
        var clientCache = new Dictionary<string, IAmazonS3>();

        foreach (var (bucketId, opts) in _bucketConfigs)
        {
            var clientKey = $"{opts.ServiceUrl}|{opts.AccessKey}|{opts.SecretKey}";

            if (!clientCache.TryGetValue(clientKey, out var client))
            {
                var config = new AmazonS3Config
                {
                    ServiceURL = opts.ServiceUrl,
                    ForcePathStyle = opts.ForcePathStyle,
                };

                client = new AmazonS3Client(new BasicAWSCredentials(opts.AccessKey, opts.SecretKey), config);
                clientCache[clientKey] = client;
            }

            _clientsByBucketName[opts.BucketName] = client;
        }

        _uniqueClients = clientCache.Values.ToList();
    }

    /// <summary>
    /// Получает имя бакета для указанного типа файла (реальное имя из конфигурации)
    /// </summary>
    public string GetBucketName(UploadFileType fileType)
    {
        var bucketId = GetBucketId(fileType);

        return _bucketConfigs.TryGetValue(bucketId, out var config)
            ? config.BucketName
            : bucketId;
    }

    /// <summary>
    /// Получает S3 клиент для указанного имени бакета
    /// </summary>
    public IAmazonS3 GetClientForBucket(string bucketName)
    {
        if (_clientsByBucketName.TryGetValue(bucketName, out var client))
            return client;

        throw new InvalidOperationException($"S3 клиент не настроен для бакета: {bucketName}");
    }

    /// <summary>
    /// Возвращает все бакеты с их S3 клиентами для инициализации
    /// </summary>
    public IEnumerable<(string BucketName, IAmazonS3 Client)> GetAllBuckets()
    {
        return _bucketConfigs.Select(x => (x.Value.BucketName, _clientsByBucketName[x.Value.BucketName]));
    }

    /// <summary>
    /// Получает все уникальные идентификаторы бакетов из маппинга типов файлов
    /// </summary>
    public static IEnumerable<string> GetAllBucketIds()
    {
        return BucketIdMap.Values.Distinct();
    }

    private static string GetBucketId(UploadFileType fileType)
    {
        return BucketIdMap.TryGetValue(fileType, out var id)
            ? id
            : BucketIdMap[UploadFileType.Unknown];
    }

    public void Dispose()
    {
        foreach (var client in _uniqueClients)
        {
            client.Dispose();
        }
    }
}
