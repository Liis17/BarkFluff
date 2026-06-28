using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarkFluff.Calls.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CallSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CallerUserId = table.Column<long>(type: "bigint", nullable: false),
                    CalleeUserId = table.Column<long>(type: "bigint", nullable: true),
                    ChatId = table.Column<Guid>(type: "uuid", nullable: true),
                    RoomName = table.Column<string>(type: "text", nullable: false),
                    Media = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    EndReason = table.Column<int>(type: "integer", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AnsweredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EndedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EndedByUserId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CallSessions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CallSessions_CalleeUserId",
                table: "CallSessions",
                column: "CalleeUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CallSessions_ChatId",
                table: "CallSessions",
                column: "ChatId");

            migrationBuilder.CreateIndex(
                name: "IX_CallSessions_Status",
                table: "CallSessions",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CallSessions");
        }
    }
}
