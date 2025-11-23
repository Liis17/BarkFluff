using Amazon.S3;
using Amazon.S3.Model;
using BarkFluff.Files.Domain;

namespace BarkFluff.Files.Infrastructure;

/// <summary>
/// Сервис для автоматической инициализации S3 бакетов при запуске приложения
/// </summary>
public class S3BucketInitializer
{
    private readonly IAmazonS3 _s3Client;
    private readonly ILogger<S3BucketInitializer> _logger;

    public S3BucketInitializer(IAmazonS3 s3Client, ILogger<S3BucketInitializer> logger)
    {
        _s3Client = s3Client;
        _logger = logger;
    }

    /// <summary>
    /// Инициализирует все необходимые S3 бакеты
    /// </summary>
    public async Task InitializeBucketsAsync()
    {
        _logger.LogInformation("Начинается инициализация S3 бакетов...");

        // Получаем все уникальные имена бакетов из S3BucketHelper
        var bucketNames = GetAllBucketNames();

        foreach (var bucketName in bucketNames)
        {
            try
            {
                await EnsureBucketExistsAsync(bucketName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при инициализации бакета {BucketName}", bucketName);
                throw;
            }
        }

        _logger.LogInformation("Инициализация S3 бакетов успешно завершена");
    }

    /// <summary>
    /// Проверяет существование бакета и создает его при необходимости
    /// </summary>
    private async Task EnsureBucketExistsAsync(string bucketName)
    {
        try
        {
            // Проверяем существование бакета
            var bucketExists = await _s3Client.DoesS3BucketExistAsync(bucketName);

            if (bucketExists)
            {
                _logger.LogInformation("Бакет {BucketName} уже существует", bucketName);
                return;
            }

            // Создаем бакет
            _logger.LogInformation("Создание бакета {BucketName}...", bucketName);
            var putBucketRequest = new PutBucketRequest
            {
                BucketName = bucketName,
                UseClientRegion = false
            };

            await _s3Client.PutBucketAsync(putBucketRequest);
            _logger.LogInformation("Бакет {BucketName} успешно создан", bucketName);

            // Устанавливаем политику доступа для публичного чтения
            await SetBucketPolicyAsync(bucketName);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            // Бакет уже существует (может быть создан параллельно)
            _logger.LogWarning("Бакет {BucketName} уже существует (конфликт при создании)", bucketName);
        }
    }

    /// <summary>
    /// Устанавливает политику публичного чтения для бакета
    /// </summary>
    private async Task SetBucketPolicyAsync(string bucketName)
    {
        try
        {
            var policy = $$"""
            {
                "Version": "2012-10-17",
                "Statement": [
                    {
                        "Effect": "Allow",
                        "Principal": {
                            "AWS": "*"
                        },
                        "Action": "s3:GetObject",
                        "Resource": "arn:aws:s3:::{{bucketName}}/*"
                    }
                ]
            }
            """;

            var request = new PutBucketPolicyRequest
            {
                BucketName = bucketName,
                Policy = policy
            };

            await _s3Client.PutBucketPolicyAsync(request);
            _logger.LogInformation("Политика доступа для бакета {BucketName} установлена", bucketName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Не удалось установить политику доступа для бакета {BucketName}", bucketName);
            // Не бросаем исключение, так как бакет уже создан
        }
    }

    /// <summary>
    /// Получает все уникальные имена бакетов из S3BucketHelper
    /// </summary>
    private static HashSet<string> GetAllBucketNames()
    {
        var bucketNames = new HashSet<string>();

        // Проходим по всем типам файлов и собираем уникальные имена бакетов
        foreach (UploadFileType fileType in Enum.GetValues<UploadFileType>())
        {
            var bucketName = S3BucketHelper.GetBucketName(fileType);
            bucketNames.Add(bucketName);
        }

        return bucketNames;
    }
}
