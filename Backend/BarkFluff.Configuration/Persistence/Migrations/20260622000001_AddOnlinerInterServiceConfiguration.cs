using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarkFluff.Configuration.Persistence.Migrations
{
    /// <summary>
    /// Inter-service ключи для Onliner (ServiceId = 9).
    /// MessagesService:* нужен ChatMembershipFilter (проверка членства в чате для typing/онлайн-статуса),
    /// UsersService:* — OnlineVisibilityFilter (настройки приватности онлайна).
    /// Значения заполняет ConfigurationDefaultsPopulator при старте Configuration.
    /// </summary>
    public partial class AddOnlinerInterServiceConfiguration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                INSERT INTO ""Configurations"" (""Section"", ""Key"", ""Value"", ""EditedAt"", ""EditedBy"", ""EditedFrom"", ""ServiceId"")
                VALUES
                    -- Messages: проверка членства в чате (ChatMembershipFilter)
                    ('MessagesService', 'Host', '', NOW(), 'system', 'migration', 9),
                    ('MessagesService', 'Token', '', NOW(), 'system', 'migration', 9),

                    -- Users: настройки видимости онлайна (OnlineVisibilityFilter)
                    ('UsersService', 'Host', '', NOW(), 'system', 'migration', 9),
                    ('UsersService', 'Token', '', NOW(), 'system', 'migration', 9);
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DELETE FROM ""Configurations""
                WHERE ""ServiceId"" = 9
                  AND ""EditedFrom"" = 'migration'
                  AND ""Section"" IN ('MessagesService', 'UsersService');
            ");
        }
    }
}
