using BarkFluff.Configuration.Infrastructure;

using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarkFluff.Configuration.Persistence.Migrations
{
    /// <summary>
    /// Ключи скачивания federated-файлов для Federation (ServiceId = 15) — этап 3.2,
    /// docs/rearch/phase-3/step-3.2-fetchfile-access.md:
    /// - FilesService:Host/Token — Federation зовёт FilesServerApi.FetchFileStream при отдаче
    ///   файла ноде-партнёру. Ключ хранится в бакете ПОТРЕБИТЕЛЯ, не вызываемого сервиса.
    /// - Federation:FetchFileRateLimitPerOrigin — лимит запросов файлов с одной ноды в минуту.
    /// - Federation:S2SConnectTimeout / RemoteFileIdleTimeout — таймауты стрима.
    /// Значения заполняет ConfigurationDefaultsPopulator при старте Configuration.
    /// </summary>
    [DbContext(typeof(ConfigurationContext))]
    [Migration("20260728020000_AddFederationFileConfiguration")]
    public partial class AddFederationFileConfiguration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                INSERT INTO ""Configurations"" (""Section"", ""Key"", ""Value"", ""EditedAt"", ""EditedBy"", ""EditedFrom"", ""ServiceId"")
                SELECT v.""Section"", v.""Key"", '', NOW(), 'system', 'migration', 15
                FROM (VALUES
                    ('FilesService', 'Host'),
                    ('FilesService', 'Token'),
                    ('Federation', 'FetchFileRateLimitPerOrigin'),
                    ('Federation', 'S2SConnectTimeout'),
                    ('Federation', 'RemoteFileIdleTimeout')
                ) AS v(""Section"", ""Key"")
                WHERE NOT EXISTS (
                    SELECT 1 FROM ""Configurations"" c
                    WHERE c.""ServiceId"" = 15 AND c.""Section"" = v.""Section"" AND c.""Key"" = v.""Key""
                );
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DELETE FROM ""Configurations""
                WHERE ""ServiceId"" = 15
                  AND ""EditedFrom"" = 'migration'
                  AND (""Section"" = 'FilesService'
                       OR (""Section"" = 'Federation'
                           AND ""Key"" IN ('FetchFileRateLimitPerOrigin', 'S2SConnectTimeout', 'RemoteFileIdleTimeout')));
            ");
        }
    }
}
