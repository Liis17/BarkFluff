using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarkFluff.Messages.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFederatedAttachmentSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Снапшот метаданных federated-вложения (этап 3.1, docs/rearch/06-files.md).
            // Файлы не реплицируются — байты живут только на origin-ноде; реплицируется снапшот,
            // чтобы сообщение рендерилось без единого сетевого похода на чужую ноду.
            // Backfill не нужен: все существующие строки — локальные, новые колонки у них NULL.

            // NULL = локальный файл (существующее поведение), NOT NULL = байты на origin.
            migrationBuilder.AddColumn<string>(
                name: "OriginServer",
                table: "MessageAttachments",
                type: "text",
                nullable: true);

            // Только для remote: у локальных filename по-прежнему берётся из Files при рендере.
            migrationBuilder.AddColumn<string>(
                name: "FileName",
                table: "MessageAttachments",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PreviewFileId",
                table: "MessageAttachments",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ImageWidth",
                table: "MessageAttachments",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ImageHeight",
                table: "MessageAttachments",
                type: "integer",
                nullable: true);

            // Проверки доступа к fed-файлу (этапы 3.2/3.3) ищут вложение по FileId —
            // без индекса это seq scan по всем вложениям ноды на каждое скачивание.
            migrationBuilder.CreateIndex(
                name: "IX_MessageAttachments_FileId",
                table: "MessageAttachments",
                column: "FileId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "IX_MessageAttachments_FileId", table: "MessageAttachments");

            migrationBuilder.DropColumn(name: "ImageHeight", table: "MessageAttachments");
            migrationBuilder.DropColumn(name: "ImageWidth", table: "MessageAttachments");
            migrationBuilder.DropColumn(name: "PreviewFileId", table: "MessageAttachments");
            migrationBuilder.DropColumn(name: "FileName", table: "MessageAttachments");
            migrationBuilder.DropColumn(name: "OriginServer", table: "MessageAttachments");
        }
    }
}
