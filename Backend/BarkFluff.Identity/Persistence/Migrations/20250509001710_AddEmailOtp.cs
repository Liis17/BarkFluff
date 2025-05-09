using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarkFluff.Identity.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEmailOtp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "EmailOtpEnabled",
                table: "AuthUserProperties",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "LastEmailAuthCode",
                table: "AuthUserProperties",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "SelectedOtpType",
                table: "AuthUserProperties",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EmailOtpEnabled",
                table: "AuthUserProperties");

            migrationBuilder.DropColumn(
                name: "LastEmailAuthCode",
                table: "AuthUserProperties");

            migrationBuilder.DropColumn(
                name: "SelectedOtpType",
                table: "AuthUserProperties");
        }
    }
}
