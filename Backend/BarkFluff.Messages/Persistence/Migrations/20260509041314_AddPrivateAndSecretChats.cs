using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace BarkFluff.Messages.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPrivateAndSecretChats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "KdfSalt",
                table: "Chats",
                type: "bytea",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "PassphraseVerifier",
                table: "Chats",
                type: "bytea",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Type",
                table: "Chats",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "EncryptedMessages",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ChatId = table.Column<Guid>(type: "uuid", nullable: false),
                    SenderId = table.Column<long>(type: "bigint", nullable: false),
                    SenderDeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    SentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Ciphertext = table.Column<byte[]>(type: "bytea", nullable: false),
                    Nonce = table.Column<byte[]>(type: "bytea", nullable: false),
                    AssociatedData = table.Column<byte[]>(type: "bytea", nullable: false),
                    IsEdited = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    EditedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EncryptedMessages", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EncryptedMessages_ChatId",
                table: "EncryptedMessages",
                column: "ChatId");

            migrationBuilder.CreateIndex(
                name: "IX_EncryptedMessages_ChatId_SentAt",
                table: "EncryptedMessages",
                columns: new[] { "ChatId", "SentAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EncryptedMessages");

            migrationBuilder.DropColumn(
                name: "KdfSalt",
                table: "Chats");

            migrationBuilder.DropColumn(
                name: "PassphraseVerifier",
                table: "Chats");

            migrationBuilder.DropColumn(
                name: "Type",
                table: "Chats");
        }
    }
}
