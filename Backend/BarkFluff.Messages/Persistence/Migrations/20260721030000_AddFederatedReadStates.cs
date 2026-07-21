using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarkFluff.Messages.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFederatedReadStates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Прочтения remote-участников fed-DM (docs/rearch/05-chat-replication.md, «Read receipts»,
            // этап 2.4). Локальные читатели остаются в Message.ReadBy — эта таблица только для remote.
            migrationBuilder.CreateTable(
                name: "FederatedReadStates",
                columns: table => new
                {
                    ChatId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserUuid = table.Column<Guid>(type: "uuid", nullable: false),
                    LastReadFederatedMessageId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReadAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FederatedReadStates", x => new { x.ChatId, x.UserUuid });
                });

            // LWW tie-break последующих ApplyFederatedEdit/Delete (docs/rearch/05, «Метка последнего
            // изменения»): (origin_ts_ms, OriginServer, EventId) события, применённого последним к сообщению.
            migrationBuilder.AddColumn<string>(
                name: "OriginServer",
                table: "FederatedMessageEvents",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "EventId",
                table: "FederatedMessageEvents",
                type: "uuid",
                nullable: false,
                defaultValue: Guid.Empty);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "EventId", table: "FederatedMessageEvents");
            migrationBuilder.DropColumn(name: "OriginServer", table: "FederatedMessageEvents");

            migrationBuilder.DropTable(name: "FederatedReadStates");
        }
    }
}
