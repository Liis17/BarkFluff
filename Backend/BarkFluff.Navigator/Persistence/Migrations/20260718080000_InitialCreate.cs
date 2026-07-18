using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarkFluff.Navigator.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Servers",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", Npgsql.EntityFrameworkCore.PostgreSQL.Metadata.NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BeaconHost = table.Column<string>(type: "text", nullable: false),
                    BeaconPort = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    ServerPublicName = table.Column<string>(type: "text", nullable: false),
                    Location = table.Column<string>(type: "text", nullable: false),
                    ColorLiteHex = table.Column<string>(type: "text", nullable: false),
                    ColorMainHex = table.Column<string>(type: "text", nullable: false),
                    ColorHardHex = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AddedBy = table.Column<string>(type: "text", nullable: false),
                    LastSeenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ServerName = table.Column<string>(type: "text", nullable: true),
                    FederationEndpoint = table.Column<string>(type: "text", nullable: true),
                    TlsSpkiSha256 = table.Column<string[]>(type: "text[]", nullable: true),
                    FederationProtocolVersions = table.Column<int[]>(type: "integer[]", nullable: true),
                    SigningKeys = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Servers", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Servers_ServerName",
                table: "Servers",
                column: "ServerName",
                unique: true,
                filter: "\"ServerName\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Servers");
        }
    }
}
