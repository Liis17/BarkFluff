using BarkFluff.Configuration.Persistence;

using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace BarkFluff.Configuration.Persistence.Migrations
{
    /// <summary>
    /// Добавляет конфигурацию RunSettings:Port для Web-сервиса (ServiceId = 11).
    /// </summary>
    [DbContext(typeof(ConfigurationContext))]
    [Migration("20260318000000_AddWebServiceConfiguration")]
    public partial class AddWebServiceConfiguration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                INSERT INTO ""Configurations"" (""Section"", ""Key"", ""Value"", ""EditedAt"", ""EditedBy"", ""EditedFrom"", ""ServiceId"")
                SELECT 'RunSettings', 'Port', '', NOW(), 'system', 'migration', 11
                WHERE NOT EXISTS (
                    SELECT 1 FROM ""Configurations""
                    WHERE ""ServiceId"" = 11 AND ""Section"" = 'RunSettings' AND ""Key"" = 'Port'
                );
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
