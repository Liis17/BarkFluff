using Microsoft.EntityFrameworkCore.Migrations;

namespace BarkFluff.Configuration.Persistence.Migrations
{
    /// <summary>
    /// Добавляет конфигурацию MessagesService для CloudMessaging (ServiceId = 10).
    /// CloudMessaging обращается к Messages для получения информации о чатах.
    /// </summary>
    public partial class AddCloudMessagingMessagesServiceConfiguration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                INSERT INTO ""Configurations"" (""Section"", ""Key"", ""Value"", ""EditedAt"", ""EditedBy"", ""EditedFrom"", ""ServiceId"")
                VALUES
                    ('MessagesService', 'Host', '', NOW(), 'system', 'migration', 10),
                    ('MessagesService', 'Token', '', NOW(), 'system', 'migration', 10),
                    ('UsersService', 'Host', '', NOW(), 'system', 'migration', 10),
                    ('UsersService', 'Token', '', NOW(), 'system', 'migration', 10);
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DELETE FROM ""Configurations""
                WHERE ""Section"" IN ('MessagesService', 'UsersService') AND ""ServiceId"" = 10;
            ");
        }
    }
}
