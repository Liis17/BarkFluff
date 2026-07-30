using BarkFluff.Configuration.Persistence;

using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace BarkFluff.Configuration.Persistence.Migrations
{
    /// <summary>
    /// Добавляет конфигурацию IdentityService для Bots (ServiceId = 14).
    /// Bots вызывает Identity для выпуска bot-JWT (CreateBotTokenServer).
    /// </summary>
    [DbContext(typeof(ConfigurationContext))]
    [Migration("20260715100000_AddBotsIdentityServiceConfiguration")]
    public partial class AddBotsIdentityServiceConfiguration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                INSERT INTO ""Configurations"" (""Section"", ""Key"", ""Value"", ""EditedAt"", ""EditedBy"", ""EditedFrom"", ""ServiceId"")
                SELECT v.""Section"", v.""Key"", '', NOW(), 'system', 'migration', 14
                FROM (VALUES
                    ('IdentityService', 'Host'),
                    ('IdentityService', 'Token')
                ) AS v(""Section"", ""Key"")
                WHERE NOT EXISTS (
                    SELECT 1 FROM ""Configurations"" c
                    WHERE c.""ServiceId"" = 14 AND c.""Section"" = v.""Section"" AND c.""Key"" = v.""Key""
                );
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DELETE FROM ""Configurations""
                WHERE ""Section"" = 'IdentityService' AND ""ServiceId"" = 14;
            ");
        }
    }
}
