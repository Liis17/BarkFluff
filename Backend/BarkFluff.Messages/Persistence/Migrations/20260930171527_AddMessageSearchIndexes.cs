using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarkFluff.Messages.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMessageSearchIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:pg_trgm", ",,");

            migrationBuilder.CreateIndex(
                name: "IX_Messages_Content_Text_Search",
                table: "Messages",
                column: "Content_Text",
                filter: "\"IsDeleted\" = FALSE AND \"Type\" <> 2")
                .Annotation("Npgsql:CreatedConcurrently", true)
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "IX_ChatMembers_UserId_ChatId",
                table: "ChatMembers",
                columns: new[] { "UserId", "ChatId" },
                filter: "\"UserId\" IS NOT NULL")
                .Annotation("Npgsql:CreatedConcurrently", true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Messages_Content_Text_Search",
                table: "Messages");

            migrationBuilder.DropIndex(
                name: "IX_ChatMembers_UserId_ChatId",
                table: "ChatMembers");

            migrationBuilder.AlterDatabase()
                .OldAnnotation("Npgsql:PostgresExtension:pg_trgm", ",,");
        }
    }
}
