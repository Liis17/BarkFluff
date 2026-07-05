using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarkFluff.Users.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUserIsBot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsBot",
                table: "Users",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsBot",
                table: "Users");
        }
    }
}
