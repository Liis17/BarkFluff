using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarkFluff.Navigator.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddServerPublicName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ServerPublicName",
                table: "Servers",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ServerPublicName",
                table: "Servers");
        }
    }
}
