using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarkFluff.Users.Migrations
{
    /// <inheritdoc />
    public partial class AddCaseInsensitiveUsernameEmailIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Уникальные функциональные индексы для case-insensitive Username/Email.
            // 1) Поддерживают запросы вида WHERE LOWER("Username") = LOWER(@p) (см. UsersStorage).
            // 2) Гарантируют регистронезависимую уникальность — страховка от гонки check-then-act (BUG-02).
            migrationBuilder.Sql(@"CREATE UNIQUE INDEX IF NOT EXISTS ix_users_username_lower ON ""Users"" (LOWER(""Username""));");
            migrationBuilder.Sql(@"CREATE UNIQUE INDEX IF NOT EXISTS ix_usercontacts_email_lower ON ""UserContacts"" (LOWER(""Email""));");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ix_users_username_lower;");
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ix_usercontacts_email_lower;");
        }
    }
}
