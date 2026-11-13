using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarkFluff.Users.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountRestrictions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AccountStatus",
                table: "Users",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "CleanupCompletedAt",
                table: "Users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletionScheduledAt",
                table: "Users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RestrictedAt",
                table: "Users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RestrictionReason",
                table: "Users",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PreDeletionAccountStatus",
                table: "Users",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PreDeletionRestrictionReason",
                table: "Users",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PreDeletionRestrictedAt",
                table: "Users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_AccountStatus_DeletionScheduledAt",
                table: "Users",
                columns: new[] { "AccountStatus", "DeletionScheduledAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Users_AccountStatus_DeletionScheduledAt",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "AccountStatus",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "CleanupCompletedAt",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "DeletionScheduledAt",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "RestrictedAt",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "RestrictionReason",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "PreDeletionAccountStatus",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "PreDeletionRestrictionReason",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "PreDeletionRestrictedAt",
                table: "Users");
        }
    }
}
