using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarkFluff.Users.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLegalConsent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Принятая редакция Пользовательского соглашения и Политики конфиденциальности —
            // дата «Последнее обновление» из шапки документа, как в Android (acceptedLegalRevision).
            // Обе колонки nullable: у существующих пользователей согласие не фиксировалось.
            migrationBuilder.AddColumn<string>(
                name: "AcceptedLegalRevision",
                table: "Users",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "AcceptedLegalAt",
                table: "Users",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AcceptedLegalRevision",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "AcceptedLegalAt",
                table: "Users");
        }
    }
}
