using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarkFluff.Identity.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AuthenticationRegistrationNames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FirstName",
                table: "AuthenticationChallenges",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "LastName",
                table: "AuthenticationChallenges",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FirstName",
                table: "AuthenticationChallenges");

            migrationBuilder.DropColumn(
                name: "LastName",
                table: "AuthenticationChallenges");
        }
    }
}
