using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarkFluff.Files.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MakeFileHashUnique : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Дедупликация существующих строк ПЕРЕД созданием уникального индекса.
            // Дубликаты могли накопиться из-за гонки read-then-write (несколько FileId на один Hash).
            // Оставляем одну строку на каждый Hash (с минимальным ctid), остальные удаляем —
            // иначе CREATE UNIQUE INDEX упадёт при накате на непустую таблицу с дублями.
            migrationBuilder.Sql(@"
                DELETE FROM ""FileHashes"" a
                USING ""FileHashes"" b
                WHERE a.ctid > b.ctid
                  AND a.""Hash"" = b.""Hash"";");

            migrationBuilder.DropIndex(
                name: "IX_FileHashes_Hash",
                table: "FileHashes");

            migrationBuilder.CreateIndex(
                name: "IX_FileHashes_Hash",
                table: "FileHashes",
                column: "Hash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Удалённые в Up дубликаты не восстанавливаются — возвращаем только неуникальный индекс.
            migrationBuilder.DropIndex(
                name: "IX_FileHashes_Hash",
                table: "FileHashes");

            migrationBuilder.CreateIndex(
                name: "IX_FileHashes_Hash",
                table: "FileHashes",
                column: "Hash");
        }
    }
}
