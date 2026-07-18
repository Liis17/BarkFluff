using BarkFluff.Configuration.Infrastructure;

using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarkFluff.Configuration.Persistence.Migrations
{
    /// <summary>
    /// Этап 1.3 rearch: окно допустимого рассинхрона часов для XFed (Federation:SignatureWindowSeconds).
    /// Дефолт (300) читается в коде XFedServerInterceptor, populator не меняется.
    /// </summary>
    [DbContext(typeof(ConfigurationContext))]
    [Migration("20260718060000_AddFederationSignatureWindowConfiguration")]
    public partial class AddFederationSignatureWindowConfiguration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                INSERT INTO ""Configurations"" (""Section"", ""Key"", ""Value"", ""EditedAt"", ""EditedBy"", ""EditedFrom"", ""ServiceId"")
                SELECT 'Federation', 'SignatureWindowSeconds', '', NOW(), 'system', 'migration', 15
                WHERE NOT EXISTS (
                    SELECT 1 FROM ""Configurations"" c
                    WHERE c.""ServiceId"" = 15 AND c.""Section"" = 'Federation' AND c.""Key"" = 'SignatureWindowSeconds'
                );
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DELETE FROM ""Configurations""
                WHERE ""ServiceId"" = 15
                  AND ""Section"" = 'Federation'
                  AND ""Key"" = 'SignatureWindowSeconds'
                  AND ""EditedBy"" = 'system' AND ""EditedFrom"" = 'migration';
            ");
        }
    }
}
