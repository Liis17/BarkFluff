using Microsoft.EntityFrameworkCore.Migrations;

namespace BarkFluff.Configuration.Persistence.Migrations
{
    /// <summary>
    /// Добавляет конфигурацию IdentityService для FastAuth (ServiceId = 7).
    /// FastAuth вызывает Identity для создания сессии после подтверждения QR-входа.
    /// </summary>
    public partial class AddFastAuthIdentityServiceConfiguration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                INSERT INTO ""Configurations"" (""Section"", ""Key"", ""Value"", ""EditedAt"", ""EditedBy"", ""EditedFrom"", ""ServiceId"")
                VALUES
                    ('IdentityService', 'Host', '', NOW(), 'system', 'migration', 7),
                    ('IdentityService', 'Token', '', NOW(), 'system', 'migration', 7);
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DELETE FROM ""Configurations""
                WHERE ""Section"" = 'IdentityService' AND ""ServiceId"" = 7;
            ");
        }
    }
}
