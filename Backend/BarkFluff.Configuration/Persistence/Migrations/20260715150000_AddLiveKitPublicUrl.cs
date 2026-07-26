using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarkFluff.Configuration.Persistence.Migrations
{
    /// <summary>
    /// Публичный wss://-адрес LiveKit (ServiceId = 13, Calls) — отдаётся анонимным клиентам через Beacon.
    /// Отдельно от LiveKit:Url (внутренний ws://-адрес для Calls -> LiveKit).
    /// Значение заполняет ConfigurationDefaultsPopulator при старте Configuration.
    /// </summary>
    public partial class AddLiveKitPublicUrl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                INSERT INTO ""Configurations"" (""Section"", ""Key"", ""Value"", ""EditedAt"", ""EditedBy"", ""EditedFrom"", ""ServiceId"")
                VALUES
                    ('LiveKit', 'PublicUrl', '', NOW(), 'system', 'migration', 13);
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DELETE FROM ""Configurations""
                WHERE ""ServiceId"" = 13 AND ""Section"" = 'LiveKit' AND ""Key"" = 'PublicUrl';
            ");
        }
    }
}
