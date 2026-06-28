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
            // Идемпотентно: вставляем ключ только если его ещё нет (на таблице нет
            // уникального индекса по Section/Key/ServiceId, ключи могли добавить вручную).
            migrationBuilder.Sql(@"
                INSERT INTO ""Configurations"" (""Section"", ""Key"", ""Value"", ""EditedAt"", ""EditedBy"", ""EditedFrom"", ""ServiceId"")
                SELECT v.""Section"", v.""Key"", '', NOW(), 'system', 'migration', 9
                FROM (VALUES
                    -- Messages: проверка членства в чате (ChatMembershipFilter)
                    ('MessagesService', 'Host'),
                    ('MessagesService', 'Token'),
                    -- Users: настройки видимости онлайна (OnlineVisibilityFilter)
                    ('UsersService', 'Host'),
                    ('UsersService', 'Token')
                ) AS v(""Section"", ""Key"")
                WHERE NOT EXISTS (
                    SELECT 1 FROM ""Configurations"" c
                    WHERE c.""ServiceId"" = 9 AND c.""Section"" = v.""Section"" AND c.""Key"" = v.""Key""
                );
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
