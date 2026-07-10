using BarkFluff.Configuration.Infrastructure;

using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarkFluff.Configuration.Persistence.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ConfigurationContext))]
    [Migration("20260308000000_AddConfigurationServiceTokenForAdminPanel")]
    public partial class AddConfigurationServiceTokenForAdminPanel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                INSERT INTO ""Configurations"" (""Section"", ""Key"", ""Value"", ""EditedAt"", ""EditedBy"", ""EditedFrom"", ""ServiceId"")
                SELECT v.""Section"", v.""Key"", v.""Value"", NOW(), 'system', 'migration', 0
                FROM (VALUES
                    ('ConfigurationService', 'Token', ''),
                    ('ConfigurationService', 'Host', 'http://configuration:7003'),
                    ('IdentityService', 'Token', ''),
                    ('IdentityService', 'Host', 'http://identity:7000')
                ) AS v(""Section"", ""Key"", ""Value"")
                WHERE NOT EXISTS (
                    SELECT 1 FROM ""Configurations"" c
                    WHERE c.""ServiceId"" = 0 AND c.""Section"" = v.""Section"" AND c.""Key"" = v.""Key""
                );

                UPDATE ""Configurations""
                SET ""Value"" = 'http://configuration:7003', ""EditedAt"" = NOW()
                WHERE ""ServiceId"" = 0
                  AND ""Section"" = 'ConfigurationService'
                  AND ""Key"" = 'Host'
                  AND ""Value"" = 'http://configuration:7010';
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DELETE FROM ""Configurations""
                WHERE ""Section"" IN ('ConfigurationService', 'IdentityService') AND ""ServiceId"" = 0 AND ""EditedBy"" = 'system' AND ""EditedFrom"" = 'migration';
            ");
        }
    }
}
