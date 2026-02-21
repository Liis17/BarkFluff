using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarkFluff.Configuration.Persistence.Migrations
{
    /// <summary>
    /// Добавляет конфигурацию S3-бакета для хранения PNG-картинок бейджей.
    /// Бакет badge-images хранит изображения без сжатия с поддержкой альфа-канала.
    /// ServiceUrl, AccessKey и SecretKey будут автоматически заполнены ConfigurationDefaultsPopulator при старте.
    /// </summary>
    public partial class AddBadgeImagesBucketConfiguration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                INSERT INTO ""Configurations"" (""Section"", ""Key"", ""Value"", ""EditedAt"", ""EditedBy"", ""EditedFrom"", ""ServiceId"")
                VALUES
                    -- badge-images (PNG-картинки бейджей, постоянные ссылки, без JPEG-сжатия)
                    ('S3Buckets:badge-images', 'ServiceUrl', '', NOW(), 'system', 'migration', 5),
                    ('S3Buckets:badge-images', 'AccessKey', '', NOW(), 'system', 'migration', 5),
                    ('S3Buckets:badge-images', 'SecretKey', '', NOW(), 'system', 'migration', 5),
                    ('S3Buckets:badge-images', 'BucketName', 'badge-images', NOW(), 'system', 'migration', 5);
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DELETE FROM ""Configurations""
                WHERE ""Section"" = 'S3Buckets:badge-images'
                  AND ""ServiceId"" = 5;
            ");
        }
    }
}
