using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarkFluff.Users.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPrimaryBadgeAndMiniImage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsPrimary",
                table: "UserBadges",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "MiniImageUrl",
                table: "Badges",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserBadges_UserId_Primary",
                table: "UserBadges",
                column: "UserId",
                unique: true,
                filter: "\"IsPrimary\" = TRUE");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UserBadges_UserId_Primary",
                table: "UserBadges");

            migrationBuilder.DropColumn(
                name: "IsPrimary",
                table: "UserBadges");

            migrationBuilder.DropColumn(
                name: "MiniImageUrl",
                table: "Badges");
        }
    }
}
