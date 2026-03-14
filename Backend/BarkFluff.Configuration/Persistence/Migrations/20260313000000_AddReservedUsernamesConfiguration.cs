using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarkFluff.Configuration.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReservedUsernamesConfiguration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                INSERT INTO ""Configurations"" (""Section"", ""Key"", ""Value"", ""EditedAt"", ""EditedBy"", ""EditedFrom"", ""ServiceId"")
                VALUES
                    ('ReservedNames', 'Usernames', 'admin,support,help,system,moderator,mod,root,superuser,administrator,official,barkfluff,bark,fluff', NOW(), 'system', 'migration', 2);
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DELETE FROM ""Configurations""
                WHERE ""Section"" = 'ReservedNames' AND ""Key"" = 'Usernames' AND ""ServiceId"" = 2 AND ""EditedBy"" = 'system' AND ""EditedFrom"" = 'migration';
            ");
        }
    }
}
