using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarkFluff.Users.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRemoteUsers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Кеш профилей пользователей чужих нод (docs/rearch/01-addressing-identity.md, этап 2.1).
            // PK = Uuid (UUID с домашней ноды пользователя), UNIQUE (Username, ServerName) —
            // при коллизии побеждает свежий резолв (старая запись переименовывается).
            migrationBuilder.CreateTable(
                name: "RemoteUsers",
                columns: table => new
                {
                    Uuid = table.Column<Guid>(type: "uuid", nullable: false),
                    Username = table.Column<string>(type: "text", nullable: false),
                    ServerName = table.Column<string>(type: "text", nullable: false),
                    FirstName = table.Column<string>(type: "text", nullable: true),
                    LastName = table.Column<string>(type: "text", nullable: true),
                    Bio = table.Column<string>(type: "text", nullable: true),
                    AvatarFileId = table.Column<string>(type: "text", nullable: true),
                    IsDeactivated = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    LastSyncedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RemoteUsers", x => x.Uuid);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RemoteUsers_Username_ServerName",
                table: "RemoteUsers",
                columns: new[] { "Username", "ServerName" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "RemoteUsers");
        }
    }
}
