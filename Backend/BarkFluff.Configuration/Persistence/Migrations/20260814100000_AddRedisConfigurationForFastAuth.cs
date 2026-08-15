using BarkFluff.Configuration.Persistence;

using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarkFluff.Configuration.Persistence.Migrations
{
    /// <summary>
    /// Redis для FastAuth (ServiceId = 7), см. BarkFluff.Shared.Identity.ServiceId.
    /// Сервис читает Configuration["Redis"] и падает при старте без него
    /// (Redis-стор QR-сессий + pub/sub wake-up стримов, см. docs/scaling/fastauth.md),
    /// но ключ заводился только для Messages (6), Federation (15), Onliner (9) и Bots (14).
    /// Значение подставит ConfigurationDefaultsPopulator (default "redis:6379" при старте Configuration).
    /// </summary>
    [DbContext(typeof(ConfigurationContext))]
    [Migration("20260814100000_AddRedisConfigurationForFastAuth")]
    public partial class AddRedisConfigurationForFastAuth : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                INSERT INTO ""Configurations"" (""Section"", ""Key"", ""Value"", ""EditedAt"", ""EditedBy"", ""EditedFrom"", ""ServiceId"")
                SELECT 'Redis', '', '', NOW(), 'system', 'migration', 7
                WHERE NOT EXISTS (
                    SELECT 1 FROM ""Configurations"" c
                    WHERE c.""ServiceId"" = 7 AND c.""Section"" = 'Redis' AND c.""Key"" = ''
                );
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DELETE FROM ""Configurations""
                WHERE ""ServiceId"" = 7
                  AND ""EditedFrom"" = 'migration'
                  AND ""Section"" = 'Redis'
                  AND ""Key"" = '';
            ");
        }
    }
}
