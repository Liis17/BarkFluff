using BarkFluff.Configuration.Persistence;

using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarkFluff.Configuration.Persistence.Migrations
{
    /// <summary>
    /// Ключи Federation (ServiceId = 15, см. BarkFluff.Shared.Identity.ServiceId) для presence-моста
    /// (этап 4.3, docs/rearch/phase-4/step-4.3-federation-presence.md):
    /// - OnlinerService:Host/Token — Federation зовёт OnlinerServerApi.GetLocalPresence (отдача
    ///   статусов ноде-партнёру) и UpsertRemoteStatus (вливание полученных). Ключ хранится в бакете
    ///   ПОТРЕБИТЕЛЯ, не вызываемого сервиса.
    /// - Federation:Presence* — параметры лимитов, сверки, дебаунса, coalescing и ресинка.
    /// Значения заполняет ConfigurationDefaultsPopulator при старте Configuration.
    /// </summary>
    [DbContext(typeof(ConfigurationContext))]
    [Migration("20260727020000_AddFederationPresenceConfiguration")]
    public partial class AddFederationPresenceConfiguration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Идемпотентно: вставляем ключ только если его ещё нет.
            migrationBuilder.Sql(@"
                INSERT INTO ""Configurations"" (""Section"", ""Key"", ""Value"", ""EditedAt"", ""EditedBy"", ""EditedFrom"", ""ServiceId"")
                SELECT v.""Section"", v.""Key"", '', NOW(), 'system', 'migration', 15
                FROM (VALUES
                    ('OnlinerService', 'Host'),
                    ('OnlinerService', 'Token'),
                    ('Federation', 'MaxPresenceSubscriptionSize'),
                    ('Federation', 'PresenceInterestTtlSeconds'),
                    ('Federation', 'PresenceReconcileSeconds'),
                    ('Federation', 'PresenceResubscribeMinSeconds'),
                    ('Federation', 'PresenceCoalesceSeconds'),
                    ('Federation', 'PresenceResyncSeconds')
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
                  AND (""Section"" = 'OnlinerService'
                       OR (""Section"" = 'Federation' AND ""Key"" LIKE 'Presence%')
                       OR (""Section"" = 'Federation' AND ""Key"" = 'MaxPresenceSubscriptionSize'));
            ");
        }
    }
}
