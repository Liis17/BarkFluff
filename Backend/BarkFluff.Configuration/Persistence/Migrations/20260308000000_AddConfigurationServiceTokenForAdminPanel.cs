using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarkFluff.Configuration.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddConfigurationServiceTokenForAdminPanel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                INSERT INTO ""Configurations"" (""Section"", ""Key"", ""Value"", ""EditedAt"", ""EditedBy"", ""EditedFrom"", ""ServiceId"")
                VALUES
                    -- ConfigurationService для AdminPanel (ServiceId = 0)
                    ('ConfigurationService', 'Token', '', NOW(), 'system', 'migration', 0),
                    ('ConfigurationService', 'Host', 'http://configuration:7010', NOW(), 'system', 'migration', 0),
                    
                    -- IdentityService для AdminPanel (ServiceId = 0)
                    ('IdentityService', 'Token', '', NOW(), 'system', 'migration', 0),
                    ('IdentityService', 'Host', 'http://identity:7000', NOW(), 'system', 'migration', 0);
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
