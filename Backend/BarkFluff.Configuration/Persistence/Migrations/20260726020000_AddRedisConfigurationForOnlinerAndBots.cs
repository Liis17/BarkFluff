using BarkFluff.Configuration.Infrastructure;

using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarkFluff.Configuration.Persistence.Migrations
{
    /// <summary>
    /// Redis для Onliner (ServiceId = 9) и Bots (ServiceId = 14), см. BarkFluff.Shared.Identity.ServiceId.
    /// Оба сервиса читают Configuration["Redis"] и падают при старте без него
    /// (Onliner — presence-стор и single-runner, Bots — rate-limit и polling-guard),
    /// но ключ заводился только для Messages (6) и Federation (15).
    /// Значение подставит ConfigurationDefaultsPopulator (default "redis:6379" при старте Configuration).
    /// </summary>
    [DbContext(typeof(ConfigurationContext))]
    [Migration("20260726020000_AddRedisConfigurationForOnlinerAndBots")]
    public partial class AddRedisConfigurationForOnlinerAndBots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                INSERT INTO ""Configurations"" (""Section"", ""Key"", ""Value"", ""EditedAt"", ""EditedBy"", ""EditedFrom"", ""ServiceId"")
                SELECT 'Redis', '', '', NOW(), 'system', 'migration', v.""ServiceId""
                FROM (VALUES (9), (14)) AS v(""ServiceId"")
                WHERE NOT EXISTS (
                    SELECT 1 FROM ""Configurations"" c
                    WHERE c.""ServiceId"" = v.""ServiceId"" AND c.""Section"" = 'Redis' AND c.""Key"" = ''
                );
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DELETE FROM ""Configurations""
                WHERE ""ServiceId"" IN (9, 14)
                  AND ""EditedFrom"" = 'migration'
                  AND ""Section"" = 'Redis'
                  AND ""Key"" = '';
            ");
        }
    }
}
