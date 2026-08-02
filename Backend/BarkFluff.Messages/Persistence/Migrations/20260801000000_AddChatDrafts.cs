using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarkFluff.Messages.Persistence.Migrations;

public partial class AddChatDrafts : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ChatDrafts",
            columns: table => new
            {
                ChatId = table.Column<Guid>(type: "uuid", nullable: false),
                UserId = table.Column<long>(type: "bigint", nullable: false),
                Text = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                ReplyToMessageId = table.Column<long>(type: "bigint", nullable: true),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                Revision = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ChatDrafts", x => new { x.ChatId, x.UserId });
                table.ForeignKey(
                    name: "FK_ChatDrafts_Chats_ChatId",
                    column: x => x.ChatId,
                    principalTable: "Chats",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_ChatDrafts_UserId_ChatId",
            table: "ChatDrafts",
            columns: new[] { "UserId", "ChatId" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "ChatDrafts");
    }
}
