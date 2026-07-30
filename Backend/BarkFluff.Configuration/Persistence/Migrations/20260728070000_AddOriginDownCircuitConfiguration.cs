using BarkFluff.Configuration.Persistence;

using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarkFluff.Configuration.Persistence.Migrations
{
    /// <summary>
    /// UX недоступного origin (этап 3.5, docs/rearch/phase-3/step-3.5-origin-down-ux.md):
    /// - Files:FedRetryAfterSeconds (ServiceId = 5) — Retry-After в ответе 503;
    /// - Federation:RemoteFileCircuit* (ServiceId = 15) — порог и окно circuit breaker'а,
    ///   чтобы лежащая нода не съедала connect-timeout на каждом обращении.
    /// </summary>
    [DbContext(typeof(ConfigurationContext))]
    [Migration("20260728070000_AddOriginDownCircuitConfiguration")]
    public partial class AddOriginDownCircuitConfiguration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                INSERT INTO ""Configurations"" (""Section"", ""Key"", ""Value"", ""EditedAt"", ""EditedBy"", ""EditedFrom"", ""ServiceId"")
                SELECT 'Files', 'FedRetryAfterSeconds', '', NOW(), 'system', 'migration', 5
                WHERE NOT EXISTS (
                    SELECT 1 FROM ""Configurations"" c
                    WHERE c.""ServiceId"" = 5 AND c.""Section"" = 'Files' AND c.""Key"" = 'FedRetryAfterSeconds'
                );

                INSERT INTO ""Configurations"" (""Section"", ""Key"", ""Value"", ""EditedAt"", ""EditedBy"", ""EditedFrom"", ""ServiceId"")
                SELECT 'Federation', v.""Key"", '', NOW(), 'system', 'migration', 15
                FROM (VALUES
                    ('RemoteFileCircuitFailures'),
                    ('RemoteFileCircuitOpenSeconds')
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
                WHERE ""EditedFrom"" = 'migration'
                  AND ((""ServiceId"" = 5 AND ""Section"" = 'Files' AND ""Key"" = 'FedRetryAfterSeconds')
                       OR (""ServiceId"" = 15 AND ""Section"" = 'Federation'
                           AND ""Key"" IN ('RemoteFileCircuitFailures', 'RemoteFileCircuitOpenSeconds')));
            ");
        }
    }
}
