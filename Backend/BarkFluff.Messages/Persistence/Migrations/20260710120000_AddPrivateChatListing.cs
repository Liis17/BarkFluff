using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarkFluff.Messages.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPrivateChatListing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "Chats",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "CURRENT_TIMESTAMP");

            migrationBuilder.AddColumn<int>(
                name: "PrivateInviteState",
                table: "Chats",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "PrivateUserHighId",
                table: "Chats",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "PrivateUserLowId",
                table: "Chats",
                type: "bigint",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE \"Chats\" c
                SET \"PrivateInviteState\" = 1
                WHERE c.\"Type\" = 1
                  AND (SELECT COUNT(*) FROM \"ChatMembers\" m WHERE m.\"ChatId\" = c.\"Id\") = 2;
                """);

            migrationBuilder.CreateTable(
                name: "PrivateChatReadStates",
                columns: table => new
                {
                    ChatId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    LastReadMessageId = table.Column<long>(type: "bigint", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrivateChatReadStates", x => new { x.ChatId, x.UserId });
                    table.ForeignKey(
                        name: "FK_PrivateChatReadStates_Chats_ChatId",
                        column: x => x.ChatId,
                        principalTable: "Chats",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Chats_Type_PrivateUserLowId_PrivateUserHighId",
                table: "Chats",
                columns: new[] { "Type", "PrivateUserLowId", "PrivateUserHighId" },
                unique: true,
                filter: "\"Type\" = 1 AND \"PrivateUserLowId\" IS NOT NULL AND \"PrivateUserHighId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "PrivateChatReadStates");
            migrationBuilder.DropIndex(name: "IX_Chats_Type_PrivateUserLowId_PrivateUserHighId", table: "Chats");
            migrationBuilder.DropColumn(name: "CreatedAt", table: "Chats");
            migrationBuilder.DropColumn(name: "PrivateInviteState", table: "Chats");
            migrationBuilder.DropColumn(name: "PrivateUserHighId", table: "Chats");
            migrationBuilder.DropColumn(name: "PrivateUserLowId", table: "Chats");
        }
    }
}
