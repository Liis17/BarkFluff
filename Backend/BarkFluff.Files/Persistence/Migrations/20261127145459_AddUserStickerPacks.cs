using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarkFluff.Files.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUserStickerPacks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ShortName",
                table: "StickerPacks",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            // Существующим наборам — короткое имя как у новых без явного имени (StickerPackShortName.Generate).
            migrationBuilder.Sql(
                "UPDATE \"StickerPacks\" SET \"ShortName\" = 'pack_' || left(replace(\"Id\"::text, '-', ''), 12) WHERE \"ShortName\" IS NULL;");

            migrationBuilder.CreateTable(
                name: "UserStickerPacks",
                columns: table => new
                {
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    StickerPackId = table.Column<Guid>(type: "uuid", nullable: false),
                    AddedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserStickerPacks", x => new { x.UserId, x.StickerPackId });
                    table.ForeignKey(
                        name: "FK_UserStickerPacks_StickerPacks_StickerPackId",
                        column: x => x.StickerPackId,
                        principalTable: "StickerPacks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StickerPacks_ShortName",
                table: "StickerPacks",
                column: "ShortName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserStickerPacks_StickerPackId",
                table: "UserStickerPacks",
                column: "StickerPackId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserStickerPacks");

            migrationBuilder.DropIndex(
                name: "IX_StickerPacks_ShortName",
                table: "StickerPacks");

            migrationBuilder.DropColumn(
                name: "ShortName",
                table: "StickerPacks");
        }
    }
}
