using BarkFluff.Configuration.Infrastructure;

using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarkFluff.Configuration.Persistence.Migrations
{
    /// <summary>
    /// Inter-service ключ MessagesService для Federation (ServiceId = 15 — Federation, см.
    /// BarkFluff.Shared.Identity.ServiceId; по образцу AddBotsConfiguration, где тот же MessagesService
    /// заведён под ServiceId Bots = 14 — каждый потребитель хранит ключ в СВОЁМ бакете).
    /// Federation вызывает MessagesServerApi.ImportFederatedChat / ImportFederatedMessage (этап 2.3,
    /// docs/rearch/phase-2/step-2.3-messages-import.md) при маршрутизации входящих S2S-событий.
    /// Значения заполняет ConfigurationDefaultsPopulator при старте Configuration.
    /// </summary>
    [DbContext(typeof(ConfigurationContext))]
    [Migration("20260721020000_AddFederationMessagesServiceConfiguration")]
    public partial class AddFederationMessagesServiceConfiguration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Идемпотентно: вставляем ключ только если его ещё нет.
            migrationBuilder.Sql(@"
                INSERT INTO ""Configurations"" (""Section"", ""Key"", ""Value"", ""EditedAt"", ""EditedBy"", ""EditedFrom"", ""ServiceId"")
                SELECT v.""Section"", v.""Key"", '', NOW(), 'system', 'migration', 15
                FROM (VALUES
                    ('MessagesService', 'Host'),
                    ('MessagesService', 'Token')
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
                  AND ""Section"" = 'MessagesService';
            ");
        }
    }
}
