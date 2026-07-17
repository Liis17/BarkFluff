using BarkFluff.Configuration.Infrastructure;

using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarkFluff.Configuration.Persistence.Migrations
{
    /// <summary>
    /// Конфигурация будущего сервиса Federation (ServiceId = 15) + секция FederationService
    /// для будущих клиентов (ServiceId = 0). Сам сервис Federation не создаётся (Фаза 1).
    /// Значения заполняет ConfigurationDefaultsPopulator при старте Configuration.
    ///
    /// RunSettings:Host не заводится — этот паттерн упразднён миграцией
    /// 20260207000000_FixServiceIdsAndAddExternalEndpoints (глобальный RunSettings:Host удалён,
    /// ни один текущий сервис его не использует, хост биндится 0.0.0.0 в коде сервиса).
    /// </summary>
    [DbContext(typeof(ConfigurationContext))]
    [Migration("20260717000000_AddFederationConfiguration")]
    public partial class AddFederationConfiguration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Идемпотентно: вставляем ключ только если его ещё нет.
            migrationBuilder.Sql(@"
                INSERT INTO ""Configurations"" (""Section"", ""Key"", ""Value"", ""EditedAt"", ""EditedBy"", ""EditedFrom"", ""ServiceId"")
                SELECT v.""Section"", v.""Key"", '', NOW(), 'system', 'migration', 15
                FROM (VALUES
                    -- gRPC-порт (7030)
                    ('RunSettings', 'Port'),

                    -- Строка подключения к БД федерации
                    ('FederationDb', ''),

                    -- DNS-домен ноды (оператор обязан задать сам)
                    ('Federation', 'ServerName'),

                    -- Включение федерации (по умолчанию false до Фазы 1+)
                    ('Federation', 'Enabled'),

                    -- Публичный S2S-адрес (оператор обязан задать сам)
                    ('Federation', 'ExternalEndpoint')
                ) AS v(""Section"", ""Key"")
                WHERE NOT EXISTS (
                    SELECT 1 FROM ""Configurations"" c
                    WHERE c.""ServiceId"" = 15 AND c.""Section"" = v.""Section"" AND c.""Key"" = v.""Key""
                );

                -- FederationService для будущих клиентов (ServiceId = 0)
                INSERT INTO ""Configurations"" (""Section"", ""Key"", ""Value"", ""EditedAt"", ""EditedBy"", ""EditedFrom"", ""ServiceId"")
                SELECT v.""Section"", v.""Key"", '', NOW(), 'system', 'migration', 0
                FROM (VALUES
                    ('FederationService', 'Host'),
                    ('FederationService', 'Token')
                ) AS v(""Section"", ""Key"")
                WHERE NOT EXISTS (
                    SELECT 1 FROM ""Configurations"" c
                    WHERE c.""ServiceId"" = 0 AND c.""Section"" = v.""Section"" AND c.""Key"" = v.""Key""
                );
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DELETE FROM ""Configurations""
                WHERE ""ServiceId"" = 15 AND ""EditedBy"" = 'system' AND ""EditedFrom"" = 'migration';

                DELETE FROM ""Configurations""
                WHERE ""ServiceId"" = 0 AND ""Section"" = 'FederationService' AND ""EditedBy"" = 'system' AND ""EditedFrom"" = 'migration';
            ");
        }
    }
}
