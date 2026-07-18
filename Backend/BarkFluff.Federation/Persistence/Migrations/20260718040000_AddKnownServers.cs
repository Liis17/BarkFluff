using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarkFluff.Federation.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddKnownServers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "KnownServers",
                columns: table => new
                {
                    ServerName = table.Column<string>(type: "text", nullable: false),
                    FederationEndpoint = table.Column<string>(type: "text", nullable: false),
                    TlsSpkiSha256 = table.Column<string[]>(type: "text[]", nullable: false),
                    Source = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    FirstSeenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastSeenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastKeyRefreshAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ProtocolVersion = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KnownServers", x => x.ServerName);
                });

            migrationBuilder.CreateTable(
                name: "KnownServerKeys",
                columns: table => new
                {
                    ServerName = table.Column<string>(type: "text", nullable: false),
                    KeyId = table.Column<string>(type: "text", nullable: false),
                    PublicKey = table.Column<byte[]>(type: "bytea", nullable: false),
                    ExpiredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RevokedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KnownServerKeys", x => new { x.ServerName, x.KeyId });
                    table.ForeignKey(
                        name: "FK_KnownServerKeys_KnownServers_ServerName",
                        column: x => x.ServerName,
                        principalTable: "KnownServers",
                        principalColumn: "ServerName",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "KnownServerKeys");

            migrationBuilder.DropTable(
                name: "KnownServers");
        }
    }
}
