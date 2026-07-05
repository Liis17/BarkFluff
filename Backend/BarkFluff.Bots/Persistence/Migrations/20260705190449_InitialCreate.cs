using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace BarkFluff.Bots.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BotFatherSessions",
                columns: table => new
                {
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false),
                    ContextBotId = table.Column<long>(type: "bigint", nullable: true),
                    PendingName = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BotFatherSessions", x => x.UserId);
                });

            migrationBuilder.CreateTable(
                name: "Bots",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false),
                    OwnerUserId = table.Column<long>(type: "bigint", nullable: true),
                    Username = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    TokenHash = table.Column<string>(type: "text", nullable: false),
                    SystemRole = table.Column<int>(type: "integer", nullable: false),
                    LastConfirmedUpdateId = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Bots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BotUpdates",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BotId = table.Column<long>(type: "bigint", nullable: false),
                    Payload = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BotUpdates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BotUpdates_Bots_BotId",
                        column: x => x.BotId,
                        principalTable: "Bots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Bots_OwnerUserId",
                table: "Bots",
                column: "OwnerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Bots_SystemRole",
                table: "Bots",
                column: "SystemRole",
                unique: true,
                filter: "\"SystemRole\" <> 0");

            migrationBuilder.CreateIndex(
                name: "IX_BotUpdates_BotId_Id",
                table: "BotUpdates",
                columns: new[] { "BotId", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BotFatherSessions");

            migrationBuilder.DropTable(
                name: "BotUpdates");

            migrationBuilder.DropTable(
                name: "Bots");
        }
    }
}
