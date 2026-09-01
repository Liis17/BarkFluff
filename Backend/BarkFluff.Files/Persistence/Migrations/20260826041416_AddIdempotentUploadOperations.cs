using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarkFluff.Files.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIdempotentUploadOperations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UploadOperations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientOperationId = table.Column<Guid>(type: "uuid", nullable: true),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    ReservedFileId = table.Column<Guid>(type: "uuid", nullable: false),
                    ResultFileId = table.Column<Guid>(type: "uuid", nullable: true),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false),
                    LeaseToken = table.Column<Guid>(type: "uuid", nullable: true),
                    LeaseExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UploadOperations", x => x.Id);
                });

            migrationBuilder.Sql(
                """
                INSERT INTO "UploadOperations"
                    ("Id", "ClientOperationId", "UserId", "ReservedFileId", "ResultFileId",
                     "Type", "State", "LeaseToken", "LeaseExpiresAt", "CreatedAt", "UpdatedAt")
                SELECT
                    "Id",
                    NULL,
                    COALESCE("Uploaders"[1], 0),
                    "Id",
                    CASE WHEN NULLIF("Etag", '') IS NOT NULL THEN "Id" ELSE NULL END,
                    "Type",
                    CASE WHEN NULLIF("Etag", '') IS NOT NULL THEN 2 ELSE 0 END,
                    NULL,
                    NULL,
                    "CreatedAt",
                    "CreatedAt"
                FROM "UploadedFiles";
                """);

            migrationBuilder.CreateIndex(
                name: "IX_UploadOperations_ReservedFileId",
                table: "UploadOperations",
                column: "ReservedFileId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UploadOperations_State_LeaseExpiresAt",
                table: "UploadOperations",
                columns: new[] { "State", "LeaseExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_UploadOperations_UserId_ClientOperationId",
                table: "UploadOperations",
                columns: new[] { "UserId", "ClientOperationId" },
                unique: true,
                filter: "\"ClientOperationId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UploadOperations");
        }
    }
}
