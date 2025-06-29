using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarkFluff.Users.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFullTextSearch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Создание расширения для полнотекстового поиска (pg_trgm)
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS pg_trgm;");
        
            // Создание индекса GIN для полнотекстового поиска
            migrationBuilder.Sql(@"
            CREATE INDEX users_fulltext_idx ON ""Users"" USING gin(
                to_tsvector('russian', ""FirstName"" || ' ' || ""LastName"" || ' ' || ""Username"")
            );
        ");
        
            // Создание индексов для триграмм
            migrationBuilder.Sql(@"CREATE INDEX users_firstname_trgm_idx ON ""Users"" USING gin (""FirstName"" gin_trgm_ops);");
            migrationBuilder.Sql(@"CREATE INDEX users_lastname_trgm_idx ON ""Users"" USING gin (""LastName"" gin_trgm_ops);");
            migrationBuilder.Sql(@"CREATE INDEX users_username_trgm_idx ON ""Users"" USING gin (""Username"" gin_trgm_ops);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Удаление индекса полнотекстового поиска
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS users_fulltext_idx;");
        
            // Удаление индексов триграмм
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS users_firstname_trgm_idx;");
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS users_lastname_trgm_idx;");
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS users_username_trgm_idx;");
        }
    }
}
