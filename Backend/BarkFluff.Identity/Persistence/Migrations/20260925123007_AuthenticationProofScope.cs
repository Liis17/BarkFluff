using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarkFluff.Identity.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AuthenticationProofScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ProofScope",
                table: "AuthenticationChallenges",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ProofScope",
                table: "AuthenticationChallenges");
        }
    }
}
