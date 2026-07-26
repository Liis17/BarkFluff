using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarkFluff.Bots.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBotCommands : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Commands",
                table: "Bots",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Commands",
                table: "Bots");
        }
    }
}
