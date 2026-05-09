using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace BarkFluff.Users.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddChatFolders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ChatFolders",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OwnerUserId = table.Column<long>(type: "bigint", nullable: false),
                    FolderId = table.Column<Guid>(type: "uuid", nullable: false),
                    FolderName = table.Column<string>(type: "text", nullable: false),
                    FolderIcon = table.Column<string>(type: "text", nullable: true),
                    ChatList = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatFolders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChatFolders_Users_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChatFolders_FolderId",
                table: "ChatFolders",
                column: "FolderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChatFolders_OwnerUserId",
                table: "ChatFolders",
                column: "OwnerUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "ChatFolders");
        }
    }
}
