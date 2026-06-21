using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarkFluff.Configuration.Persistence.Migrations
{
    /// <summary>
    /// Конфигурация сервиса звонков Calls (ServiceId = 13).
    /// Значения заполняет ConfigurationDefaultsPopulator при старте Configuration.
    /// LiveKit:* — креды для подписи токенов и верификации webhooks (совпадают с keys в livekit.yaml).
    /// </summary>
    public partial class AddCallsConfiguration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                INSERT INTO ""Configurations"" (""Section"", ""Key"", ""Value"", ""EditedAt"", ""EditedBy"", ""EditedFrom"", ""ServiceId"")
                VALUES
                    -- gRPC-порт (7025) и HTTP/1.1-порт для LiveKit-webhooks (7026)
                    ('RunSettings', 'Port', '', NOW(), 'system', 'migration', 13),
                    ('RunSettings', 'Http1Port', '', NOW(), 'system', 'migration', 13),

                    -- Строка подключения к БД CDR
                    ('CallsDb', '', '', NOW(), 'system', 'migration', 13),

                    -- Внешний субдомен (nginx)
                    ('ExternalEndpoint', 'Host', '', NOW(), 'system', 'migration', 13),

                    -- Messages: авторизация группового звонка и список участников для ринга
                    ('MessagesService', 'Host', '', NOW(), 'system', 'migration', 13),
                    ('MessagesService', 'Token', '', NOW(), 'system', 'migration', 13),

                    -- LiveKit SFU
                    ('LiveKit', 'Url', '', NOW(), 'system', 'migration', 13),
                    ('LiveKit', 'ApiKey', '', NOW(), 'system', 'migration', 13),
                    ('LiveKit', 'ApiSecret', '', NOW(), 'system', 'migration', 13);
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DELETE FROM ""Configurations""
                WHERE ""ServiceId"" = 13 AND ""EditedBy"" = 'system' AND ""EditedFrom"" = 'migration';
            ");
        }
    }
}
