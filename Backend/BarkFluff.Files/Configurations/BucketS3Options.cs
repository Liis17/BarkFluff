namespace BarkFluff.Files.Configurations;

/// <summary>
/// Настройки S3 хранилища для конкретного бакета.
/// Каждый бакет может находиться на отдельном S3-совместимом хранилище.
/// </summary>
public class BucketS3Options
{
    /// <summary>
    /// URL сервиса S3-совместимого хранилища (например, http://minio:9000)
    /// </summary>
    public string ServiceUrl { get; set; }

    /// <summary>
    /// Ключ доступа к хранилищу
    /// </summary>
    public string AccessKey { get; set; }

    /// <summary>
    /// Секретный ключ хранилища
    /// </summary>
    public string SecretKey { get; set; }

    /// <summary>
    /// Имя бакета на S3-совместимом хранилище
    /// </summary>
    public string BucketName { get; set; }

    /// <summary>
    /// Использовать path-style доступ (необходимо для MinIO)
    /// </summary>
    public bool ForcePathStyle => true;
}
