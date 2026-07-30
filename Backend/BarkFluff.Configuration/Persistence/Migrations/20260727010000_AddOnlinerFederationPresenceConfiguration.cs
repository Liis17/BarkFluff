using BarkFluff.Configuration.Persistence;

using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarkFluff.Configuration.Persistence.Migrations
{
    /// <summary>
    /// Ключи Onliner (ServiceId = 9, см. BarkFluff.Shared.Identity.ServiceId) для uuid-ветки
    /// presence (этап 4.2, docs/rearch/phase-4/step-4.2-onliner-uuid-branch.md):
    /// - FederationService:Host/Token — Onliner зовёт FederationInternalApi.SetPresenceInterest
    ///   (интерес к remote-presence) и DeliverTypingOutbound (этап 4.4). Ключ хранится в бакете
    ///   ПОТРЕБИТЕЛЯ, не вызываемого сервиса — тот же приём, что в AddFederationMessagesServiceConfiguration.
    /// - Onliner:RemotePresenceTtlSeconds — TTL кеша remote-статусов (эвикция uuid без подписчиков).
    /// - Onliner:PresenceInterestIntervalSeconds — период heartbeat'а интереса.
    /// Значения заполняет ConfigurationDefaultsPopulator при старте Configuration.
    /// Пустой FederationService:Host гейтит всю федеративную ветку Onliner — нода без федерации
    /// не поднимает ни клиента, ни фоновый репортер.
    /// </summary>
    [DbContext(typeof(ConfigurationContext))]
    [Migration("20260727010000_AddOnlinerFederationPresenceConfiguration")]
    public partial class AddOnlinerFederationPresenceConfiguration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Идемпотентно: вставляем ключ только если его ещё нет.
            migrationBuilder.Sql(@"
                INSERT INTO ""Configurations"" (""Section"", ""Key"", ""Value"", ""EditedAt"", ""EditedBy"", ""EditedFrom"", ""ServiceId"")
                SELECT v.""Section"", v.""Key"", '', NOW(), 'system', 'migration', 9
                FROM (VALUES
                    ('FederationService', 'Host'),
                    ('FederationService', 'Token'),
                    ('Onliner', 'RemotePresenceTtlSeconds'),
                    ('Onliner', 'PresenceInterestIntervalSeconds')
                ) AS v(""Section"", ""Key"")
                WHERE NOT EXISTS (
                    SELECT 1 FROM ""Configurations"" c
                    WHERE c.""ServiceId"" = 9 AND c.""Section"" = v.""Section"" AND c.""Key"" = v.""Key""
                );
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DELETE FROM ""Configurations""
                WHERE ""ServiceId"" = 9
                  AND ""EditedFrom"" = 'migration'
                  AND (""Section"" = 'FederationService'
                       OR (""Section"" = 'Onliner'
                           AND ""Key"" IN ('RemotePresenceTtlSeconds', 'PresenceInterestIntervalSeconds')));
            ");
        }
    }
}
