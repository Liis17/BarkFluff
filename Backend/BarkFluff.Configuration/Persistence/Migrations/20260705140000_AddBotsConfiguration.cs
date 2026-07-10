using BarkFluff.Configuration.Infrastructure;

using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarkFluff.Configuration.Persistence.Migrations
{
    /// <summary>
    /// Конфигурация сервиса ботов Bots (ServiceId = 14) + секция BotsService для AdminPanel (ServiceId = 0).
    /// Значения заполняет ConfigurationDefaultsPopulator при старте Configuration.
    /// </summary>
    [DbContext(typeof(ConfigurationContext))]
    [Migration("20260705140000_AddBotsConfiguration")]
    public partial class AddBotsConfiguration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Идемпотентно: вставляем ключ только если его ещё нет.
            migrationBuilder.Sql(@"
                INSERT INTO ""Configurations"" (""Section"", ""Key"", ""Value"", ""EditedAt"", ""EditedBy"", ""EditedFrom"", ""ServiceId"")
                SELECT v.""Section"", v.""Key"", '', NOW(), 'system', 'migration', 14
                FROM (VALUES
                    -- gRPC-порт (7027) и HTTP/1.1-порт для Bot REST API (7028)
                    ('RunSettings', 'Port'),
                    ('RunSettings', 'Http1Port'),

                    -- Строка подключения к БД ботов
                    ('BotsDb', ''),

                    -- Внешний субдомен (nginx)
                    ('ExternalEndpoint', 'Host'),

                    -- Users: создание бот-юзеров, публичные профили (getUserInfo)
                    ('UsersService', 'Host'),
                    ('UsersService', 'Token'),

                    -- Messages: отправка сообщений от имени ботов (SendMessageServer)
                    ('MessagesService', 'Host'),
                    ('MessagesService', 'Token'),

                    -- Files: загрузка вложений ботов (UploadFileServer), аватары (UploadAvatarServer)
                    ('FilesService', 'Host'),
                    ('FilesService', 'Token')
                ) AS v(""Section"", ""Key"")
                WHERE NOT EXISTS (
                    SELECT 1 FROM ""Configurations"" c
                    WHERE c.""ServiceId"" = 14 AND c.""Section"" = v.""Section"" AND c.""Key"" = v.""Key""
                );

                -- BotsService для AdminPanel (ServiceId = 0)
                INSERT INTO ""Configurations"" (""Section"", ""Key"", ""Value"", ""EditedAt"", ""EditedBy"", ""EditedFrom"", ""ServiceId"")
                SELECT v.""Section"", v.""Key"", '', NOW(), 'system', 'migration', 0
                FROM (VALUES
                    ('BotsService', 'Host'),
                    ('BotsService', 'Token')
                ) AS v(""Section"", ""Key"")
                WHERE NOT EXISTS (
                    SELECT 1 FROM ""Configurations"" c
                    WHERE c.""ServiceId"" = 0 AND c.""Section"" = v.""Section"" AND c.""Key"" = v.""Key""
                );
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DELETE FROM ""Configurations""
                WHERE ""ServiceId"" = 14 AND ""EditedBy"" = 'system' AND ""EditedFrom"" = 'migration';

                DELETE FROM ""Configurations""
                WHERE ""ServiceId"" = 0 AND ""Section"" = 'BotsService' AND ""EditedBy"" = 'system' AND ""EditedFrom"" = 'migration';
            ");
        }
    }
}
