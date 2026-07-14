using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarkFluff.Bots.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceBotTokenHashWithTokenId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TokenHash",
                table: "Bots");

            migrationBuilder.AddColumn<string>(
                name: "TokenId",
                table: "Bots",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TokenId",
                table: "Bots");

            migrationBuilder.AddColumn<string>(
                name: "TokenHash",
                table: "Bots",
                type: "text",
                nullable: false,
                defaultValue: "");
        }
    }
}
