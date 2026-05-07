using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarkFluff.Identity.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SecurityHardening : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Why: для существующих записей ставим текущее UTC-время — они мгновенно
            // станут просроченными, что семантически верно (старые reset-запросы и так
            // не должны быть валидными после введения проверки срока).
            migrationBuilder.AddColumn<DateTime>(
                name: "ExpiresAt",
                table: "ResetPasswords",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "NOW() AT TIME ZONE 'UTC'");

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_Value",
                table: "RefreshTokens",
                column: "Value",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RefreshTokens_Value",
                table: "RefreshTokens");

            migrationBuilder.DropColumn(
                name: "ExpiresAt",
                table: "ResetPasswords");
        }
    }
}
