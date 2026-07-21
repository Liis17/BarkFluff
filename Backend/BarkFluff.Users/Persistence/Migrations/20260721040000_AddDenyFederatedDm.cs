using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarkFluff.Users.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDenyFederatedDm : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Запрет входящих федеративных DM (docs/rearch/05-chat-replication.md, этап 2.5).
            // Действует только на создание новых fed-чатов — существующие продолжают работать.
            migrationBuilder.AddColumn<bool>(
                name: "DenyFederatedDm",
                table: "Privacies",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DenyFederatedDm",
                table: "Privacies");
        }
    }
}
