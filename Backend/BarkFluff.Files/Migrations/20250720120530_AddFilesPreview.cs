using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarkFluff.Files.Migrations
{
    /// <inheritdoc />
    public partial class AddFilesPreview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PreviewId",
                table: "UploadedFiles",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "Size",
                table: "UploadedFiles",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PreviewId",
                table: "UploadedFiles");

            migrationBuilder.DropColumn(
                name: "Size",
                table: "UploadedFiles");
        }
    }
}
