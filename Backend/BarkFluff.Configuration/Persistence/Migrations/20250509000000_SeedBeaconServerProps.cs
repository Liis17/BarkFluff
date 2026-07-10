using BarkFluff.Configuration.Infrastructure;

using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarkFluff.Configuration.Persistence.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ConfigurationContext))]
    [Migration("20250509000000_SeedBeaconServerProps")]
    public partial class SeedBeaconServerProps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                INSERT INTO ""Configurations"" (""Section"", ""Key"", ""Value"", ""EditedAt"", ""EditedBy"", ""EditedFrom"", ""ServiceId"")
                SELECT v.""Section"", v.""Key"", '', NOW(), 'System', 'Migration', 3
                FROM (VALUES
                    ('ServerProps', 'Name'),
                    ('ServerProps', 'Description'),
                    ('ServerProps', 'PublicName')
                ) AS v(""Section"", ""Key"")
                WHERE NOT EXISTS (
                    SELECT 1 FROM ""Configurations"" c
                    WHERE c.""ServiceId"" = 3 AND c.""Section"" = v.""Section"" AND c.""Key"" = v.""Key""
                );
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DELETE FROM ""Configurations""
                WHERE ""ServiceId"" = 3
                  AND ""Section"" = 'ServerProps'
                  AND ""Key"" IN ('Name', 'Description', 'PublicName')
                  AND ""EditedBy"" = 'System'
                  AND ""EditedFrom"" = 'Migration';
            ");
        }
    }
}
