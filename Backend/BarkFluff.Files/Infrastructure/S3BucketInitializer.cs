using Amazon.S3;
using Amazon.S3.Model;

namespace BarkFluff.Files.Infrastructure;

/// <summary>
/// Сервис для автоматической инициализации S3 бакетов при запуске приложения.
/// Поддерживает бакеты на разных S3-совместимых хранилищах.
/// </summary>
public class S3BucketInitializer
{
    private readonly IS3BucketRegistry _registry;
    private readonly ILogger<S3BucketInitializer> _logger;

    public S3BucketInitializer(IS3BucketRegistry registry, ILogger<S3BucketInitializer> logger)
    {
        _registry = registry;
        _logger = logger;
    }

    /// <summary>
    /// Инициализирует все необходимые S3 бакеты
    /// </summary>
    public async Task InitializeBucketsAsync()
    {
        _logger.LogInformation("Начинается инициализация S3 бакетов...");

        foreach (var (bucketName, client) in _registry.GetAllBuckets())
        {
            try
            {
                await EnsureBucketExistsAsync(client, bucketName);
            }
            catch (Exception ex)
            {
                // S3 может быть временно недоступна — не роняем старт сервиса.
                // Чаты/сообщения и метаданные файлов (БД) продолжат работать,
                // недоступны будут только загрузка/скачивание содержимого из S3.
                _logger.LogError(ex, "Ошибка при инициализации бакета {BucketName}. Сервис продолжит запуск без гарантии доступности S3.", bucketName);
            }
        }

        _logger.LogInformation("Инициализация S3 бакетов завершена");
    }

    /// <summary>
    /// Проверяет существование бакета и создает его при необходимости
    /// </summary>
    private async Task EnsureBucketExistsAsync(IAmazonS3 client, string bucketName)
    {
        // Проверяем существование бакета через попытку получить его локацию
        try
        {
            await client.GetBucketLocationAsync(bucketName);
            _logger.LogInformation("Бакет {BucketName} уже существует", bucketName);
            return;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // Бакет не найден, продолжаем создание
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            // Токен без прав администрирования бакетов (например, R2 API-токен только на чтение/запись
            // объектов) не может проверить существование бакета. Считаем, что бакет создан заранее вручную.
            _logger.LogWarning("Нет прав на проверку бакета {BucketName} (403 Forbidden) — токен ограничен правами объектов, бакет должен быть создан заранее вручную", bucketName);
            return;
        }

        // Создаем бакет
        _logger.LogInformation("Создание бакета {BucketName}...", bucketName);
        try
        {
            var putBucketRequest = new PutBucketRequest
            {
                BucketName = bucketName,
                UseClientRegion = false
            };

            await client.PutBucketAsync(putBucketRequest);
            _logger.LogInformation("Бакет {BucketName} успешно создан", bucketName);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            // Бакет уже существует (может быть создан параллельно)
            _logger.LogWarning("Бакет {BucketName} уже существует (конфликт при создании)", bucketName);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            _logger.LogWarning("Нет прав на создание бакета {BucketName} (403 Forbidden) — создайте бакет вручную в S3-хранилище", bucketName);
        }
    }
}
