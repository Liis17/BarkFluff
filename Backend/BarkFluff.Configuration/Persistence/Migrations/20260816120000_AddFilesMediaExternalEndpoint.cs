using BarkFluff.Configuration.Persistence;

using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarkFluff.Configuration.Persistence.Migrations
{
    /// <summary>
    /// ExternalEndpoint:MediaHost для Files (ServiceId = 5) — отдельный публичный адрес
    /// файлового HTTP (upload/download), не проходящий через Cloudflare с его лимитом
    /// 100 МБ на файл. ExternalEndpoint:Host остаётся адресом gRPC и старых ссылок.
    ///
    /// Значение по умолчанию пустое: пока оператор ноды не задал адрес, Beacon отдаёт
    /// клиентам пустую строку и те работают по-прежнему через ExternalEndpoint:Host.
    /// </summary>
    [DbContext(typeof(ConfigurationContext))]
    [Migration("20260816120000_AddFilesMediaExternalEndpoint")]
    public partial class AddFilesMediaExternalEndpoint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                INSERT INTO ""Configurations"" (""Section"", ""Key"", ""Value"", ""EditedAt"", ""EditedBy"", ""EditedFrom"", ""ServiceId"")
                SELECT 'ExternalEndpoint', 'MediaHost', '', NOW(), 'system', 'migration', 5
                WHERE NOT EXISTS (
                    SELECT 1 FROM ""Configurations"" c
                    WHERE c.""ServiceId"" = 5 AND c.""Section"" = 'ExternalEndpoint' AND c.""Key"" = 'MediaHost'
                );
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DELETE FROM ""Configurations""
                WHERE ""ServiceId"" = 5
                  AND ""EditedFrom"" = 'migration'
                  AND ""Section"" = 'ExternalEndpoint'
                  AND ""Key"" = 'MediaHost';
            ");
        }
    }
}
