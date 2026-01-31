using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarkFluff.Configuration.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOnlinerDbConfiguration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var now = DateTime.UtcNow;

            migrationBuilder.Sql($@"
                INSERT INTO ""Configurations"" (""Section"", ""Key"", ""Value"", ""EditedAt"", ""EditedBy"", ""EditedFrom"", ""ServiceId"")
                VALUES
                    -- OnlinerDb connection string
                    ('OnlinerDb', '', '', '{now:yyyy-MM-dd HH:mm:ss}', 'system', 'migration', 9),
                    
                    -- RunSettings Port for Onliner (ServiceId 9)
                    ('RunSettings', 'Port', '', '{now:yyyy-MM-dd HH:mm:ss}', 'system', 'migration', 9);
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DELETE FROM ""Configurations""
                WHERE (""Section"" = 'OnlinerDb' AND ""ServiceId"" = 9)
                   OR (""Section"" = 'RunSettings' AND ""Key"" = 'Port' AND ""ServiceId"" = 9);
            ");
        }
    }
}
