using BarkFluff.Configuration.Persistence;

using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarkFluff.Configuration.Persistence.Migrations
{
    /// <summary>
    /// Конфигурация портала Developers (ServiceId = 12).
    /// Значения заполняет ConfigurationDefaultsPopulator при старте Configuration.
    /// </summary>
    [DbContext(typeof(ConfigurationContext))]
    [Migration("20260826100000_AddDevelopersConfiguration")]
    public partial class AddDevelopersConfiguration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                INSERT INTO ""Configurations"" (""Section"", ""Key"", ""Value"", ""EditedAt"", ""EditedBy"", ""EditedFrom"", ""ServiceId"")
                SELECT v.""Section"", v.""Key"", '', NOW(), 'system', 'migration', 12
                FROM (VALUES
                    -- API/gRPC-Web и SPA-порт
                    ('RunSettings', 'Port'),
                    ('RunSettings', 'Http1Port'),

                    -- Строка подключения к БД портала
                    ('DevelopersDb', ''),

                    -- Внешний адрес портала
                    ('ExternalEndpoint', 'Host')
                ) AS v(""Section"", ""Key"")
                WHERE NOT EXISTS (
                    SELECT 1 FROM ""Configurations"" c
                    WHERE c.""ServiceId"" = 12 AND c.""Section"" = v.""Section"" AND c.""Key"" = v.""Key""
                );
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DELETE FROM ""Configurations""
                WHERE ""ServiceId"" = 12
                  AND ((""Section"" = 'RunSettings' AND ""Key"" IN ('Port', 'Http1Port'))
                    OR (""Section"" = 'DevelopersDb' AND ""Key"" = '')
                    OR (""Section"" = 'ExternalEndpoint' AND ""Key"" = 'Host'))
                  AND ""EditedBy"" = 'system'
                  AND ""EditedFrom"" = 'migration';
            ");
        }
    }
}
