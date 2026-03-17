using Microsoft.EntityFrameworkCore.Migrations;

namespace BarkFluff.Configuration.Persistence.Migrations
{
    /// <summary>
    /// Добавляет конфигурацию RunSettings:Port для Web-сервиса (ServiceId = 11).
    /// </summary>
    public partial class AddWebServiceConfiguration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                INSERT INTO ""Configurations"" (""Section"", ""Key"", ""Value"", ""EditedAt"", ""EditedBy"", ""EditedFrom"", ""ServiceId"")
                VALUES
                    ('RunSettings', 'Port', '', NOW(), 'system', 'migration', 11);
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DELETE FROM ""Configurations""
                WHERE ""Section"" = 'RunSettings' AND ""Key"" = 'Port' AND ""ServiceId"" = 11;
            ");
        }
    }
}
