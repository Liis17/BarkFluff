using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarkFluff.Files.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFileHashesAndMultipleUploaders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Create FileHashes table
            migrationBuilder.CreateTable(
                name: "FileHashes",
                columns: table => new
                {
                    FileId = table.Column<Guid>(type: "uuid", nullable: false),
                    Hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FileHashes", x => x.FileId);
                });

            // Create index on Hash for fast lookups
            migrationBuilder.CreateIndex(
                name: "IX_FileHashes_Hash",
                table: "FileHashes",
                column: "Hash");

            // Change Uploader column from bigint to bigint[] (List<long>)
            // First, rename the old column
            migrationBuilder.RenameColumn(
                name: "Uploader",
                table: "UploadedFiles",
                newName: "OldUploader");

            // Add the new Uploaders column as an array
            migrationBuilder.AddColumn<List<long>>(
                name: "Uploaders",
                table: "UploadedFiles",
                type: "bigint[]",
                nullable: false,
                defaultValue: new List<long>());

            // Migrate data from OldUploader to Uploaders array
            // For non-zero uploaders, add them to the array
            // For zero uploaders (shouldn't exist but just in case), keep empty array
            migrationBuilder.Sql(
                "UPDATE \"UploadedFiles\" SET \"Uploaders\" = CASE WHEN \"OldUploader\" != 0 THEN ARRAY[\"OldUploader\"] ELSE ARRAY[]::bigint[] END");

            // Drop the old column
            migrationBuilder.DropColumn(
                name: "OldUploader",
                table: "UploadedFiles");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Add back the old Uploader column
            migrationBuilder.AddColumn<long>(
                name: "Uploader",
                table: "UploadedFiles",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            // Migrate data from Uploaders array back to Uploader (take first element)
            migrationBuilder.Sql(
                "UPDATE \"UploadedFiles\" SET \"Uploader\" = \"Uploaders\"[1] WHERE array_length(\"Uploaders\", 1) > 0");

            // Drop the Uploaders column
            migrationBuilder.DropColumn(
                name: "Uploaders",
                table: "UploadedFiles");

            // Drop the FileHashes table
            migrationBuilder.DropIndex(
                name: "IX_FileHashes_Hash",
                table: "FileHashes");

            migrationBuilder.DropTable(
                name: "FileHashes");
        }
    }
}
