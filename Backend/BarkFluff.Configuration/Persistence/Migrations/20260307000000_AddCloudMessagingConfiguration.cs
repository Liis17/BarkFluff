using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarkFluff.Configuration.Persistence.Migrations
{
    /// <summary>
    /// Добавляет конфигурацию для микросервиса CloudMessaging (ServiceId = 10).
    /// </summary>
    public partial class AddCloudMessagingConfiguration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                INSERT INTO ""Configurations"" (""Section"", ""Key"", ""Value"", ""EditedAt"", ""EditedBy"", ""EditedFrom"", ""ServiceId"")
                VALUES
                    -- RunSettings - Port for CloudMessaging service (ServiceId = 10)
                    ('RunSettings', 'Port', '7011', NOW(), 'system', 'migration', 10);
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DELETE FROM ""Configurations""
                WHERE ""ServiceId"" = 10 AND ""EditedBy"" = 'system' AND ""EditedFrom"" = 'migration';
            ");
        }
    }
}
