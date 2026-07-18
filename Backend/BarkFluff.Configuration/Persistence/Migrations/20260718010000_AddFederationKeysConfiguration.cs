using BarkFluff.Configuration.Infrastructure;

using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarkFluff.Configuration.Persistence.Migrations
{
    /// <summary>
    /// Этап 1.2 rearch: новые конфиг-ключи Federation — SPKI TLS-серта ноды, порт well-known-листенера,
    /// окно перекрытия при плановой ротации ключа. Значения по умолчанию читаются в коде Federation
    /// (пустая строка/дефолт), ConfigurationDefaultsPopulator для них не меняется.
    /// </summary>
    [DbContext(typeof(ConfigurationContext))]
    [Migration("20260718010000_AddFederationKeysConfiguration")]
    public partial class AddFederationKeysConfiguration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                INSERT INTO ""Configurations"" (""Section"", ""Key"", ""Value"", ""EditedAt"", ""EditedBy"", ""EditedFrom"", ""ServiceId"")
                SELECT v.""Section"", v.""Key"", '', NOW(), 'system', 'migration', 15
                FROM (VALUES
                    -- SPKI sha256-отпечатки TLS-серта ноды (через запятую), заполняет оператор
                    ('Federation', 'TlsSpkiSha256'),

                    -- Порт HTTP/1-листенера для /.well-known/barkfluff (дефолт 7031 в коде)
                    ('Federation', 'WellKnownPort'),

                    -- Окно перекрытия старого ключа при плановой ротации, дни (дефолт 30 в коде)
                    ('Federation', 'KeyRotationOverlapDays')
                ) AS v(""Section"", ""Key"")
                WHERE NOT EXISTS (
                    SELECT 1 FROM ""Configurations"" c
                    WHERE c.""ServiceId"" = 15 AND c.""Section"" = v.""Section"" AND c.""Key"" = v.""Key""
                );
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DELETE FROM ""Configurations""
                WHERE ""ServiceId"" = 15
                  AND ""Section"" = 'Federation'
                  AND ""Key"" IN ('TlsSpkiSha256', 'WellKnownPort', 'KeyRotationOverlapDays')
                  AND ""EditedBy"" = 'system' AND ""EditedFrom"" = 'migration';
            ");
        }
    }
}
