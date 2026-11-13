using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarkFluff.Bots.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBotOwnerRestriction : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "OwnerRestricted",
                table: "Bots",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OwnerRestricted",
                table: "Bots");
        }
    }
}
