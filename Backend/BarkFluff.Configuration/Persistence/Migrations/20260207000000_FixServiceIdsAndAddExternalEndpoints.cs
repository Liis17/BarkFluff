using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarkFluff.Configuration.Persistence.Migrations
{
    /// <summary>
    /// Миграция для исправления ServiceId и добавления внешних эндпоинтов.
    ///
    /// Изменения:
    /// 1. Перенос NavigatorUrl с ServiceId=0 на ServiceId=3 (Beacon)
    /// 2. Удаление глобального RunSettings:Host (ServiceId=0) — заменяется ExternalEndpoint
    /// 3. Добавление ExternalEndpoint:Host для каждого публичного сервиса
    /// 4. Добавление MessagesService:Host/Token для Users (ServiceId=2)
    /// 5. Удаление дублей ServerProps (оставляем из SeedInitialConfigurationKeys)
    /// </summary>
    public partial class FixServiceIdsAndAddExternalEndpoints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Перенос NavigatorUrl с ServiceId=0 на ServiceId=3 (только Beacon использует)
            migrationBuilder.Sql(@"
                UPDATE ""Configurations""
                SET ""ServiceId"" = 3, ""EditedAt"" = NOW(), ""EditedBy"" = 'system', ""EditedFrom"" = 'migration-fix'
                WHERE ""Section"" = 'NavigatorUrl' AND ""Key"" = '' AND ""ServiceId"" = 0;
            ");

            // 2. Удаление глобального RunSettings:Host (ServiceId=0)
            //    Этот параметр путал Beacon — возвращал один хост для всех сервисов.
            //    Заменяется на ExternalEndpoint:Host для каждого сервиса.
            migrationBuilder.Sql(@"
                DELETE FROM ""Configurations""
                WHERE ""Section"" = 'RunSettings' AND ""Key"" = 'Host' AND ""ServiceId"" = 0;
            ");

            // 3. Удаление глобального RunSettings:Port (ServiceId=0) — бессмысленный default
            migrationBuilder.Sql(@"
                DELETE FROM ""Configurations""
                WHERE ""Section"" = 'RunSettings' AND ""Key"" = 'Port' AND ""ServiceId"" = 0;
            ");

            // 4. Добавление ExternalEndpoint:Host для каждого публичного сервиса
            //    Внешние адреса через nginx (порт 443, субдомены)
            migrationBuilder.Sql(@"
                INSERT INTO ""Configurations"" (""Section"", ""Key"", ""Value"", ""EditedAt"", ""EditedBy"", ""EditedFrom"", ""ServiceId"")
                VALUES
                    -- Identity (1)
                    ('ExternalEndpoint', 'Host', '', NOW(), 'system', 'migration', 1),
                    -- Users (2)
                    ('ExternalEndpoint', 'Host', '', NOW(), 'system', 'migration', 2),
                    -- Beacon (3)
                    ('ExternalEndpoint', 'Host', '', NOW(), 'system', 'migration', 3),
                    -- Files (5)
                    ('ExternalEndpoint', 'Host', '', NOW(), 'system', 'migration', 5),
                    -- Messages (6)
                    ('ExternalEndpoint', 'Host', '', NOW(), 'system', 'migration', 6),
                    -- FastAuth (7)
                    ('ExternalEndpoint', 'Host', '', NOW(), 'system', 'migration', 7),
                    -- Updates (8)
                    ('ExternalEndpoint', 'Host', '', NOW(), 'system', 'migration', 8),
                    -- Onliner (9)
                    ('ExternalEndpoint', 'Host', '', NOW(), 'system', 'migration', 9);
            ");

            // 5. Добавление MessagesService:Host и MessagesService:Token для Users (ServiceId=2)
            //    Users обращается к Messages, но конфиг отсутствовал
            migrationBuilder.Sql(@"
                INSERT INTO ""Configurations"" (""Section"", ""Key"", ""Value"", ""EditedAt"", ""EditedBy"", ""EditedFrom"", ""ServiceId"")
                VALUES
                    ('MessagesService', 'Host', '', NOW(), 'system', 'migration', 2),
                    ('MessagesService', 'Token', '', NOW(), 'system', 'migration', 2);
            ");

            // 6. Удаление дублей ServerProps (миграция SeedBeaconServerProps создала их раньше,
            //    затем SeedInitialConfigurationKeys создала повторно — оставляем вторые)
            migrationBuilder.Sql(@"
                DELETE FROM ""Configurations""
                WHERE ""Id"" IN (
                    SELECT ""Id"" FROM (
                        SELECT ""Id"", ROW_NUMBER() OVER (
                            PARTITION BY ""Section"", ""Key"", ""ServiceId""
                            ORDER BY ""Id"" ASC
                        ) as rn
                        FROM ""Configurations""
                        WHERE ""Section"" = 'ServerProps' AND ""ServiceId"" = 3
                    ) sub
                    WHERE sub.rn > 1
                );
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Удаляем ExternalEndpoint записи
            migrationBuilder.Sql(@"
                DELETE FROM ""Configurations""
                WHERE ""Section"" = 'ExternalEndpoint';
            ");

            // Удаляем MessagesService записи для Users
            migrationBuilder.Sql(@"
                DELETE FROM ""Configurations""
                WHERE ""Section"" = 'MessagesService' AND ""ServiceId"" = 2;
            ");

            // Восстанавливаем глобальный RunSettings:Host
            migrationBuilder.Sql(@"
                INSERT INTO ""Configurations"" (""Section"", ""Key"", ""Value"", ""EditedAt"", ""EditedBy"", ""EditedFrom"", ""ServiceId"")
                VALUES ('RunSettings', 'Host', '', NOW(), 'system', 'migration', 0);
            ");

            // Восстанавливаем глобальный RunSettings:Port (ServiceId=0)
            migrationBuilder.Sql(@"
                INSERT INTO ""Configurations"" (""Section"", ""Key"", ""Value"", ""EditedAt"", ""EditedBy"", ""EditedFrom"", ""ServiceId"")
                VALUES ('RunSettings', 'Port', '', NOW(), 'system', 'migration', 0);
            ");

            // Восстанавливаем NavigatorUrl на ServiceId=0
            migrationBuilder.Sql(@"
                UPDATE ""Configurations""
                SET ""ServiceId"" = 0, ""EditedAt"" = NOW()
                WHERE ""Section"" = 'NavigatorUrl' AND ""Key"" = '' AND ""ServiceId"" = 3;
            ");
        }
    }
}
