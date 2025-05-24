using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarkFluff.Files.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTempFiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TempFiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OriginalFileId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TempFiles", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TempFiles_OriginalFileId",
                table: "TempFiles",
                column: "OriginalFileId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TempFiles");
        }
    }
}
