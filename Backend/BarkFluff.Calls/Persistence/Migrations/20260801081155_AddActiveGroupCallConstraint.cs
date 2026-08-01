using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarkFluff.Calls.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddActiveGroupCallConstraint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Старые дубли могли появиться до введения ограничения. Оставляем активный
            // звонок (или самый новый ринг), остальные завершаем как неудавшиеся.
            migrationBuilder.Sql("""
                WITH ranked_calls AS (
                    SELECT "Id", ROW_NUMBER() OVER (
                        PARTITION BY "ChatId"
                        ORDER BY CASE WHEN "Status" = 1 THEN 0 ELSE 1 END, "StartedAt" DESC, "Id" DESC
                    ) AS "RowNumber"
                    FROM "CallSessions"
                    WHERE "ChatId" IS NOT NULL AND "Status" IN (0, 1)
                )
                UPDATE "CallSessions" AS calls
                SET "Status" = 2, "EndReason" = 5, "EndedAt" = CURRENT_TIMESTAMP
                FROM ranked_calls
                WHERE calls."Id" = ranked_calls."Id" AND ranked_calls."RowNumber" > 1;
                """);

            migrationBuilder.DropIndex(
                name: "IX_CallSessions_ChatId",
                table: "CallSessions");

            migrationBuilder.CreateIndex(
                name: "IX_CallSessions_OneActiveGroupCall",
                table: "CallSessions",
                column: "ChatId",
                unique: true,
                filter: "\"ChatId\" IS NOT NULL AND \"Status\" IN (0, 1)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CallSessions_OneActiveGroupCall",
                table: "CallSessions");

            migrationBuilder.CreateIndex(
                name: "IX_CallSessions_ChatId",
                table: "CallSessions",
                column: "ChatId");
        }
    }
}
