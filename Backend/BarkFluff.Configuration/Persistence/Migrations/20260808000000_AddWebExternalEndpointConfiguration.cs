using BarkFluff.Configuration.Persistence;

using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarkFluff.Configuration.Persistence.Migrations
{
    /// <summary>
    /// ExternalEndpoint:Host для Web (ServiceId = 11) — миграция 20260207000000
    /// завела этот ключ для всех публичных сервисов, кроме Web (пропущен по ошибке).
    /// Без него Beacon не может заполнить web_endpoint при регистрации в Navigator.
    /// </summary>
    [DbContext(typeof(ConfigurationContext))]
    [Migration("20260808000000_AddWebExternalEndpointConfiguration")]
    public partial class AddWebExternalEndpointConfiguration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                INSERT INTO ""Configurations"" (""Section"", ""Key"", ""Value"", ""EditedAt"", ""EditedBy"", ""EditedFrom"", ""ServiceId"")
                SELECT 'ExternalEndpoint', 'Host', '', NOW(), 'system', 'migration', 11
                WHERE NOT EXISTS (
                    SELECT 1 FROM ""Configurations""
                    WHERE ""ServiceId"" = 11 AND ""Section"" = 'ExternalEndpoint' AND ""Key"" = 'Host'
                );
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DELETE FROM ""Configurations""
                WHERE ""ServiceId"" = 11 AND ""Section"" = 'ExternalEndpoint' AND ""Key"" = 'Host'
                    AND ""EditedBy"" = 'system' AND ""EditedFrom"" = 'migration';
            ");
        }
    }
}
