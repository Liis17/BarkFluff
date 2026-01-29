using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarkFluff.Configuration.Persistence.Migrations
{
    /// <summary>
    /// Миграция для перехода с общей конфигурации Minio на per-bucket конфигурацию S3.
    /// Каждый бакет теперь может иметь собственные ServiceUrl, AccessKey, SecretKey и BucketName,
    /// что позволяет размещать бакеты на разных S3-совместимых хранилищах.
    ///
    /// Бакеты:
    ///   - barkfluff-uploads    (файлы по умолчанию)
    ///   - profile-pictures     (аватарки пользователей)
    ///   - message-documents    (документы в сообщениях)
    ///   - message-videos       (видео и GIF в сообщениях)
    ///   - message-images       (изображения в сообщениях)
    ///   - chat-pictures        (обложки групповых чатов)
    /// </summary>
    public partial class AddPerBucketS3Configuration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Добавляем per-bucket конфигурацию S3 (ServiceId = 5 = Files)
            migrationBuilder.Sql(@"
                INSERT INTO ""Configurations"" (""Section"", ""Key"", ""Value"", ""EditedAt"", ""EditedBy"", ""EditedFrom"", ""ServiceId"")
                VALUES
                    -- barkfluff-uploads (файлы по умолчанию)
                    ('S3Buckets:barkfluff-uploads', 'ServiceUrl', '', NOW(), 'system', 'migration', 5),
                    ('S3Buckets:barkfluff-uploads', 'AccessKey', '', NOW(), 'system', 'migration', 5),
                    ('S3Buckets:barkfluff-uploads', 'SecretKey', '', NOW(), 'system', 'migration', 5),
                    ('S3Buckets:barkfluff-uploads', 'BucketName', 'barkfluff-uploads', NOW(), 'system', 'migration', 5),

                    -- profile-pictures (аватарки пользователей)
                    ('S3Buckets:profile-pictures', 'ServiceUrl', '', NOW(), 'system', 'migration', 5),
                    ('S3Buckets:profile-pictures', 'AccessKey', '', NOW(), 'system', 'migration', 5),
                    ('S3Buckets:profile-pictures', 'SecretKey', '', NOW(), 'system', 'migration', 5),
                    ('S3Buckets:profile-pictures', 'BucketName', 'profile-pictures', NOW(), 'system', 'migration', 5),

                    -- message-documents (документы в сообщениях)
                    ('S3Buckets:message-documents', 'ServiceUrl', '', NOW(), 'system', 'migration', 5),
                    ('S3Buckets:message-documents', 'AccessKey', '', NOW(), 'system', 'migration', 5),
                    ('S3Buckets:message-documents', 'SecretKey', '', NOW(), 'system', 'migration', 5),
                    ('S3Buckets:message-documents', 'BucketName', 'message-documents', NOW(), 'system', 'migration', 5),

                    -- message-videos (видео и GIF в сообщениях)
                    ('S3Buckets:message-videos', 'ServiceUrl', '', NOW(), 'system', 'migration', 5),
                    ('S3Buckets:message-videos', 'AccessKey', '', NOW(), 'system', 'migration', 5),
                    ('S3Buckets:message-videos', 'SecretKey', '', NOW(), 'system', 'migration', 5),
                    ('S3Buckets:message-videos', 'BucketName', 'message-videos', NOW(), 'system', 'migration', 5),

                    -- message-images (изображения в сообщениях)
                    ('S3Buckets:message-images', 'ServiceUrl', '', NOW(), 'system', 'migration', 5),
                    ('S3Buckets:message-images', 'AccessKey', '', NOW(), 'system', 'migration', 5),
                    ('S3Buckets:message-images', 'SecretKey', '', NOW(), 'system', 'migration', 5),
                    ('S3Buckets:message-images', 'BucketName', 'message-images', NOW(), 'system', 'migration', 5),

                    -- chat-pictures (обложки групповых чатов)
                    ('S3Buckets:chat-pictures', 'ServiceUrl', '', NOW(), 'system', 'migration', 5),
                    ('S3Buckets:chat-pictures', 'AccessKey', '', NOW(), 'system', 'migration', 5),
                    ('S3Buckets:chat-pictures', 'SecretKey', '', NOW(), 'system', 'migration', 5),
                    ('S3Buckets:chat-pictures', 'BucketName', 'chat-pictures', NOW(), 'system', 'migration', 5);
            ");

            // Удаляем старую общую конфигурацию Minio
            migrationBuilder.Sql(@"
                DELETE FROM ""Configurations""
                WHERE ""Section"" = 'Minio'
                  AND ""ServiceId"" = 5;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Удаляем per-bucket конфигурацию
            migrationBuilder.Sql(@"
                DELETE FROM ""Configurations""
                WHERE ""Section"" LIKE 'S3Buckets:%'
                  AND ""ServiceId"" = 5;
            ");

            // Восстанавливаем старую общую конфигурацию Minio
            migrationBuilder.Sql(@"
                INSERT INTO ""Configurations"" (""Section"", ""Key"", ""Value"", ""EditedAt"", ""EditedBy"", ""EditedFrom"", ""ServiceId"")
                VALUES
                    ('Minio', 'ServiceUrl', '', NOW(), 'system', 'migration', 5),
                    ('Minio', 'SecretKey', '', NOW(), 'system', 'migration', 5),
                    ('Minio', 'AccessKey', '', NOW(), 'system', 'migration', 5);
            ");
        }
    }
}
