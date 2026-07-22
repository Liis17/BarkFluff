using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarkFluff.Messages.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFederatedChatSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // FederatedStatus enum хранится как int (Active=0/Rejected=1/Merged=2). Default 0 (Active).
            // Существующие чаты получают Active-статус (среди существующих федеративных нет — федерация
            // включается только этим этапом).
            migrationBuilder.AddColumn<bool>(
                name: "IsFederated",
                table: "Chats",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "FederatedStatus",
                table: "Chats",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "FederatedUuidLow",
                table: "Chats",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "FederatedUuidHigh",
                table: "Chats",
                type: "uuid",
                nullable: true);

            // Анти-дубль одновременного создания fed-DM (docs/rearch/05, «Создание чата»).
            migrationBuilder.CreateIndex(
                name: "IX_Chats_FederatedUuidLow_FederatedUuidHigh",
                table: "Chats",
                columns: new[] { "FederatedUuidLow", "FederatedUuidHigh" },
                unique: true,
                filter: "\"IsFederated\" AND \"FederatedStatus\" = 0 AND \"FederatedUuidLow\" IS NOT NULL AND \"FederatedUuidHigh\" IS NOT NULL");

            // SenderId / UserId → nullable: импортированные fed-сообщения и remote-участники fed-DM
            // не имеют локального аккаунта (docs/rearch/05-chat-replication.md, этап 2.3).
            migrationBuilder.AlterColumn<long>(
                name: "SenderId",
                table: "Messages",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AlterColumn<long>(
                name: "UserId",
                table: "ChatMembers",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint");

            // Домен ноды remote-участника (punycode A-label lowercase); NULL для локального участника.
            migrationBuilder.AddColumn<string>(
                name: "ServerName",
                table: "ChatMembers",
                type: "text",
                nullable: true);

            // Последний применённый state-event входящего fed-сообщения (docs/rearch/05, catch-up 2.6).
            // Пишем начиная с 2.3 — экспорт истории отдаёт с той же подписью origin.
            migrationBuilder.CreateTable(
                name: "FederatedMessageEvents",
                columns: table => new
                {
                    ChatId = table.Column<Guid>(type: "uuid", nullable: false),
                    FederatedId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventBytes = table.Column<byte[]>(type: "bytea", nullable: false),
                    ReceivedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FederatedMessageEvents", x => new { x.ChatId, x.FederatedId });
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "FederatedMessageEvents");

            migrationBuilder.DropColumn(name: "ServerName", table: "ChatMembers");

            migrationBuilder.AlterColumn<long>(
                name: "UserId",
                table: "ChatMembers",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.AlterColumn<long>(
                name: "SenderId",
                table: "Messages",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.DropIndex(name: "IX_Chats_FederatedUuidLow_FederatedUuidHigh", table: "Chats");

            migrationBuilder.DropColumn(name: "FederatedUuidHigh", table: "Chats");
            migrationBuilder.DropColumn(name: "FederatedUuidLow", table: "Chats");
            migrationBuilder.DropColumn(name: "FederatedStatus", table: "Chats");
            migrationBuilder.DropColumn(name: "IsFederated", table: "Chats");
        }
    }
}
