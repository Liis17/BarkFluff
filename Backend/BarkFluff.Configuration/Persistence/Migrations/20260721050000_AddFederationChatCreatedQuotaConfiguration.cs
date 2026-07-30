using BarkFluff.Configuration.Persistence;

using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarkFluff.Configuration.Persistence.Migrations
{
    /// <summary>
    /// Federation:ChatCreatedHourlyLimit (ServiceId = 15 — Federation, см. BarkFluff.Shared.Identity.ServiceId)
    /// — квота ChatCreated per-origin (этап 2.5, docs/rearch/phase-2/step-2.5-privacy-antispam.md,
    /// «Изменение 4»). Значение заполняет ConfigurationDefaultsPopulator (default "100" при старте Configuration).
    /// Заодно заводим Redis (нужен ChatCreatedQuotaLimiter — Federation его раньше не использовал).
    /// </summary>
    [DbContext(typeof(ConfigurationContext))]
    [Migration("20260721050000_AddFederationChatCreatedQuotaConfiguration")]
    public partial class AddFederationChatCreatedQuotaConfiguration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                INSERT INTO ""Configurations"" (""Section"", ""Key"", ""Value"", ""EditedAt"", ""EditedBy"", ""EditedFrom"", ""ServiceId"")
                SELECT v.""Section"", v.""Key"", '', NOW(), 'system', 'migration', 15
                FROM (VALUES
                    ('Federation', 'ChatCreatedHourlyLimit'),
                    ('Redis', '')
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
                  AND ((""Section"" = 'Federation' AND ""Key"" = 'ChatCreatedHourlyLimit')
                    OR (""Section"" = 'Redis' AND ""Key"" = ''));
            ");
        }
    }
}
