using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace BarkFluff.Identity.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TelegramAuthentication : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "FastAuthTelegramEnabled",
                table: "AuthUserProperties",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastEmailAuthCodeExpiresAt",
                table: "AuthUserProperties",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LoginMode",
                table: "AuthUserProperties",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "PolicyVersion",
                table: "AuthUserProperties",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "PreferredFactor",
                table: "AuthUserProperties",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "TelegramEnabled",
                table: "AuthUserProperties",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<long>(
                name: "TelegramId",
                table: "AuthUserProperties",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "TelegramOtpEnabled",
                table: "AuthUserProperties",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "TelegramUsername",
                table: "AuthUserProperties",
                type: "text",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE "AuthUserProperties"
                SET "LoginMode" = CASE WHEN "OtpEnabled" OR "EmailOtpEnabled" THEN 3 ELSE 1 END,
                    "PreferredFactor" = CASE WHEN "OtpEnabled" THEN 1 WHEN "EmailOtpEnabled" THEN 2 ELSE 0 END,
                    "LastEmailAuthCode" = NULL;
                """);

            migrationBuilder.CreateTable(
                name: "AuthenticationChallenges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Purpose = table.Column<int>(type: "integer", nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    SecretHash = table.Column<string>(type: "text", nullable: false),
                    TelegramTokenHash = table.Column<string>(type: "text", nullable: true),
                    CodeHash = table.Column<string>(type: "text", nullable: true),
                    Factor = table.Column<int>(type: "integer", nullable: false),
                    UseRecoveryCode = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FailedAttempts = table.Column<int>(type: "integer", nullable: false),
                    PolicyVersion = table.Column<int>(type: "integer", nullable: false),
                    DeviceId = table.Column<string>(type: "text", nullable: false),
                    DeviceName = table.Column<string>(type: "text", nullable: false),
                    OperationSystem = table.Column<string>(type: "text", nullable: false),
                    AppName = table.Column<string>(type: "text", nullable: false),
                    IpAddress = table.Column<string>(type: "text", nullable: false),
                    Username = table.Column<string>(type: "text", nullable: false),
                    Email = table.Column<string>(type: "text", nullable: true),
                    PasswordHash = table.Column<string>(type: "text", nullable: true),
                    LoginMode = table.Column<int>(type: "integer", nullable: false),
                    TelegramId = table.Column<long>(type: "bigint", nullable: true),
                    TelegramUsername = table.Column<string>(type: "text", nullable: true),
                    AttemptId = table.Column<string>(type: "text", nullable: true),
                    SessionId = table.Column<long>(type: "bigint", nullable: true),
                    ProofConsumed = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuthenticationChallenges", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RecoveryCodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    Hash = table.Column<string>(type: "text", nullable: false),
                    UsedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecoveryCodes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TelegramPollingStates",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Offset = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TelegramPollingStates", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuthUserProperties_TelegramId",
                table: "AuthUserProperties",
                column: "TelegramId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuthUserProperties_UserId",
                table: "AuthUserProperties",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuthenticationChallenges_AttemptId",
                table: "AuthenticationChallenges",
                column: "AttemptId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuthenticationChallenges_ExpiresAt",
                table: "AuthenticationChallenges",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_AuthenticationChallenges_TelegramTokenHash",
                table: "AuthenticationChallenges",
                column: "TelegramTokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RecoveryCodes_UserId_Hash",
                table: "RecoveryCodes",
                columns: new[] { "UserId", "Hash" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuthenticationChallenges");

            migrationBuilder.DropTable(
                name: "RecoveryCodes");

            migrationBuilder.DropTable(
                name: "TelegramPollingStates");

            migrationBuilder.DropIndex(
                name: "IX_AuthUserProperties_TelegramId",
                table: "AuthUserProperties");

            migrationBuilder.DropIndex(
                name: "IX_AuthUserProperties_UserId",
                table: "AuthUserProperties");

            migrationBuilder.DropColumn(
                name: "FastAuthTelegramEnabled",
                table: "AuthUserProperties");

            migrationBuilder.DropColumn(
                name: "LastEmailAuthCodeExpiresAt",
                table: "AuthUserProperties");

            migrationBuilder.DropColumn(
                name: "LoginMode",
                table: "AuthUserProperties");

            migrationBuilder.DropColumn(
                name: "PolicyVersion",
                table: "AuthUserProperties");

            migrationBuilder.DropColumn(
                name: "PreferredFactor",
                table: "AuthUserProperties");

            migrationBuilder.DropColumn(
                name: "TelegramEnabled",
                table: "AuthUserProperties");

            migrationBuilder.DropColumn(
                name: "TelegramId",
                table: "AuthUserProperties");

            migrationBuilder.DropColumn(
                name: "TelegramOtpEnabled",
                table: "AuthUserProperties");

            migrationBuilder.DropColumn(
                name: "TelegramUsername",
                table: "AuthUserProperties");
        }
    }
}
