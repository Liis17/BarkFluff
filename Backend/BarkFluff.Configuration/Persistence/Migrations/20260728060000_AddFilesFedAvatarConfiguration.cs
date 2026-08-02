using BarkFluff.Configuration.Persistence;

using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarkFluff.Configuration.Persistence.Migrations
{
    /// <summary>
    /// Files:FedAvatarMaxBytes для Files (ServiceId = 5) — этап 3.4,
    /// docs/rearch/phase-3/step-3.4-remote-avatars.md. У аватара remote-пользователя нет
    /// снапшота размера (он не является вложением сообщения), поэтому объём проксируемого
    /// потока ограничивает глобальный кап.
    /// </summary>
    [DbContext(typeof(ConfigurationContext))]
    [Migration("20260728060000_AddFilesFedAvatarConfiguration")]
    public partial class AddFilesFedAvatarConfiguration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                INSERT INTO ""Configurations"" (""Section"", ""Key"", ""Value"", ""EditedAt"", ""EditedBy"", ""EditedFrom"", ""ServiceId"")
                SELECT 'Files', 'FedAvatarMaxBytes', '', NOW(), 'system', 'migration', 5
                WHERE NOT EXISTS (
                    SELECT 1 FROM ""Configurations"" c
                    WHERE c.""ServiceId"" = 5 AND c.""Section"" = 'Files' AND c.""Key"" = 'FedAvatarMaxBytes'
                );
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DELETE FROM ""Configurations""
                WHERE ""ServiceId"" = 5
                  AND ""EditedFrom"" = 'migration'
                  AND ""Section"" = 'Files'
                  AND ""Key"" = 'FedAvatarMaxBytes';
            ");
        }
    }
}
