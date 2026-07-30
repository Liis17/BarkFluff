using BarkFluff.Configuration.Persistence;

using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarkFluff.Configuration.Persistence.Migrations
{
    /// <summary>
    /// Этап 1.4 rearch: Federation тоже читает NavigatorUrl (источник 2 discovery, GetServerByName) —
    /// ранее ключ был только у Beacon (ServiceId = 3). GetDefaultValue уже умеет отдавать
    /// "http://navigator:7010" по Section+Key без привязки к ServiceId — populator не меняется.
    /// </summary>
    [DbContext(typeof(ConfigurationContext))]
    [Migration("20260718070000_AddNavigatorUrlForFederation")]
    public partial class AddNavigatorUrlForFederation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                INSERT INTO ""Configurations"" (""Section"", ""Key"", ""Value"", ""EditedAt"", ""EditedBy"", ""EditedFrom"", ""ServiceId"")
                SELECT 'NavigatorUrl', '', '', NOW(), 'system', 'migration', 15
                WHERE NOT EXISTS (
                    SELECT 1 FROM ""Configurations"" c
                    WHERE c.""ServiceId"" = 15 AND c.""Section"" = 'NavigatorUrl' AND c.""Key"" = ''
                );
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DELETE FROM ""Configurations""
                WHERE ""ServiceId"" = 15
                  AND ""Section"" = 'NavigatorUrl' AND ""Key"" = ''
                  AND ""EditedBy"" = 'system' AND ""EditedFrom"" = 'migration';
            ");
        }
    }
}
