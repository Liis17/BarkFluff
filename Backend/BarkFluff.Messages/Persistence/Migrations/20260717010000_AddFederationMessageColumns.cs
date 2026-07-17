using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarkFluff.Messages.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFederationMessageColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // LastChangeAt: NOT NULL с backfill из EditedAt/SentAt.
            // Приём: добавить nullable -> UPDATE -> ужесточить до NOT NULL.
            migrationBuilder.AddColumn<DateTime>(
                name: "LastChangeAt",
                table: "Messages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.Sql(
                """UPDATE "Messages" SET "LastChangeAt" = COALESCE("EditedAt", "SentAt");""");

            migrationBuilder.AlterColumn<DateTime>(
                name: "LastChangeAt",
                table: "Messages",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "FederatedId",
                table: "Messages",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SenderUuid",
                table: "Messages",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Messages_ChatId_FederatedId",
                table: "Messages",
                columns: new[] { "ChatId", "FederatedId" },
                unique: true,
                filter: "\"FederatedId\" IS NOT NULL");

            migrationBuilder.AddColumn<Guid>(
                name: "UserUuid",
                table: "ChatMembers",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "UserUuid",
                table: "ChatMembers");

            migrationBuilder.DropIndex(
                name: "IX_Messages_ChatId_FederatedId",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "SenderUuid",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "FederatedId",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "LastChangeAt",
                table: "Messages");
        }
    }
}
