using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarkFluff.Files.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFederatedTempFiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Capability-ссылка на federated-вложение (этап 3.3): байты живут на чужой ноде.
            // Снапшот метаданных лежит здесь же, чтобы скачивание не ходило в Messages второй раз.
            // Backfill не нужен: существующие temp-ссылки локальные, новые колонки у них NULL.
            migrationBuilder.AddColumn<string>(
                name: "OriginServer",
                table: "TempFiles",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FileName",
                table: "TempFiles",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "SizeBytes",
                table: "TempFiles",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AttachmentType",
                table: "TempFiles",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "AttachmentType", table: "TempFiles");
            migrationBuilder.DropColumn(name: "SizeBytes", table: "TempFiles");
            migrationBuilder.DropColumn(name: "FileName", table: "TempFiles");
            migrationBuilder.DropColumn(name: "OriginServer", table: "TempFiles");
        }
    }
}
