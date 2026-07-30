using BarkFluff.Configuration.Persistence;

using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarkFluff.Configuration.Persistence.Migrations
{
    /// <summary>
    /// Inter-service ключи для Files (ServiceId = 5, см. BarkFluff.Shared.Identity.ServiceId) —
    /// этап 3.3, docs/rearch/phase-3/step-3.3-fed-download.md:
    /// - MessagesService:Host/Token — Files зовёт CheckFedFileUserAccess при выдаче
    ///   capability-ссылки на federated-вложение;
    /// - FederationService:Host/Token — Files зовёт FetchRemoteFile при скачивании байтов.
    /// Ключи хранятся в бакете ПОТРЕБИТЕЛЯ, не вызываемого сервиса.
    /// Значения заполняет ConfigurationDefaultsPopulator при старте Configuration.
    /// </summary>
    [DbContext(typeof(ConfigurationContext))]
    [Migration("20260728050000_AddFilesFederationConfiguration")]
    public partial class AddFilesFederationConfiguration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                INSERT INTO ""Configurations"" (""Section"", ""Key"", ""Value"", ""EditedAt"", ""EditedBy"", ""EditedFrom"", ""ServiceId"")
                SELECT v.""Section"", v.""Key"", '', NOW(), 'system', 'migration', 5
                FROM (VALUES
                    ('MessagesService', 'Host'),
                    ('MessagesService', 'Token'),
                    ('FederationService', 'Host'),
                    ('FederationService', 'Token')
                ) AS v(""Section"", ""Key"")
                WHERE NOT EXISTS (
                    SELECT 1 FROM ""Configurations"" c
                    WHERE c.""ServiceId"" = 5 AND c.""Section"" = v.""Section"" AND c.""Key"" = v.""Key""
                );
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DELETE FROM ""Configurations""
                WHERE ""ServiceId"" = 5
                  AND ""EditedFrom"" = 'migration'
                  AND ""Section"" IN ('MessagesService', 'FederationService');
            ");
        }
    }
}
