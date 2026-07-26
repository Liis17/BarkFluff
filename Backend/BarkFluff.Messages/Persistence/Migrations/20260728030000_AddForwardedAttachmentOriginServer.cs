using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarkFluff.Messages.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddForwardedAttachmentOriginServer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Форвард federated-вложения должен сохранять ноду-владельца байтов (этап 3.3):
            // без неё проверка доступа CheckFedFileUserAccess не может точно сопоставить
            // форварднутый файл с его origin. Backfill не нужен: все существующие форварды
            // ссылаются на локальные файлы.
            migrationBuilder.AddColumn<string>(
                name: "OriginServer",
                table: "ForwardedMessageAttachment",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "OriginServer", table: "ForwardedMessageAttachment");
        }
    }
}
