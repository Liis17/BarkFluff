using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarkFluff.ClientStorage.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReleaseChannel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ClientFiles_ClientType",
                table: "ClientFiles");

            migrationBuilder.AddColumn<int>(
                name: "ReleaseChannel",
                table: "ClientFiles",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_ClientFiles_ClientType_ReleaseChannel",
                table: "ClientFiles",
                columns: new[] { "ClientType", "ReleaseChannel" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ClientFiles_ClientType_ReleaseChannel",
                table: "ClientFiles");

            migrationBuilder.DropColumn(
                name: "ReleaseChannel",
                table: "ClientFiles");

            migrationBuilder.CreateIndex(
                name: "IX_ClientFiles_ClientType",
                table: "ClientFiles",
                column: "ClientType");
        }
    }
}
