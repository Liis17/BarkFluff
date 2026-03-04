using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarkFluff.Configuration.Persistence.Migrations
{
    /// <summary>
    /// Добавляет конфигурацию S3-бакета для хранения аудиофайлов в сообщениях.
    /// ServiceUrl, AccessKey и SecretKey будут автоматически заполнены ConfigurationDefaultsPopulator при старте.
    /// </summary>
    public partial class AddAudioBucketConfiguration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                INSERT INTO ""Configurations"" (""Section"", ""Key"", ""Value"", ""EditedAt"", ""EditedBy"", ""EditedFrom"", ""ServiceId"")
                VALUES
                    -- message-audio (аудиофайлы в сообщениях)
                    ('S3Buckets:message-audio', 'ServiceUrl', '', NOW(), 'system', 'migration', 5),
                    ('S3Buckets:message-audio', 'AccessKey', '', NOW(), 'system', 'migration', 5),
                    ('S3Buckets:message-audio', 'SecretKey', '', NOW(), 'system', 'migration', 5),
                    ('S3Buckets:message-audio', 'BucketName', 'message-audio', NOW(), 'system', 'migration', 5);
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DELETE FROM ""Configurations""
                WHERE ""Section"" = 'S3Buckets:message-audio'
                  AND ""ServiceId"" = 5;
            ");
        }
    }
}
