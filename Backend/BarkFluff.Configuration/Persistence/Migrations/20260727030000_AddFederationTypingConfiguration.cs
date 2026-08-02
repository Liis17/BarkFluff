using BarkFluff.Configuration.Persistence;

using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarkFluff.Configuration.Persistence.Migrations
{
    /// <summary>
    /// Ключи typing-моста Federation (ServiceId = 15) — этап 4.4,
    /// docs/rearch/phase-4/step-4.4-typing-bridge.md:
    /// coalescing на исходящем пути, deadline S2S-вызова, лимит per-origin и TTL кеша валидации
    /// на входящем. Значения заполняет ConfigurationDefaultsPopulator при старте Configuration.
    /// </summary>
    [DbContext(typeof(ConfigurationContext))]
    [Migration("20260727030000_AddFederationTypingConfiguration")]
    public partial class AddFederationTypingConfiguration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Идемпотентно: вставляем ключ только если его ещё нет.
            migrationBuilder.Sql(@"
                INSERT INTO ""Configurations"" (""Section"", ""Key"", ""Value"", ""EditedAt"", ""EditedBy"", ""EditedFrom"", ""ServiceId"")
                SELECT 'Federation', v.""Key"", '', NOW(), 'system', 'migration', 15
                FROM (VALUES
                    ('TypingCoalesceSeconds'),
                    ('TypingDeadlineMs'),
                    ('TypingRateLimitPerOriginPerMinute'),
                    ('TypingValidationCacheSeconds')
                ) AS v(""Key"")
                WHERE NOT EXISTS (
                    SELECT 1 FROM ""Configurations"" c
                    WHERE c.""ServiceId"" = 15 AND c.""Section"" = 'Federation' AND c.""Key"" = v.""Key""
                );
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DELETE FROM ""Configurations""
                WHERE ""ServiceId"" = 15
                  AND ""EditedFrom"" = 'migration'
                  AND ""Section"" = 'Federation'
                  AND ""Key"" LIKE 'Typing%';
            ");
        }
    }
}
