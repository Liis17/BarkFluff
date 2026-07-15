using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarkFluff.Files.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPreviewIdIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_UploadedFiles_PreviewId",
                table: "UploadedFiles",
                column: "PreviewId",
                filter: "\"PreviewId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UploadedFiles_PreviewId",
                table: "UploadedFiles");
        }
    }
}
