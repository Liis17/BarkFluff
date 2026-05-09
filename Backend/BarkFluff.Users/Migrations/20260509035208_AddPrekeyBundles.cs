using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace BarkFluff.Users.Migrations
{
    /// <inheritdoc />
    public partial class AddPrekeyBundles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DevicePrekeyBundles",
                columns: table => new
                {
                    DeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    RegistrationId = table.Column<long>(type: "bigint", nullable: false),
                    IdentityPubkey = table.Column<byte[]>(type: "bytea", nullable: false),
                    SignedPrekeyId = table.Column<long>(type: "bigint", nullable: false),
                    SignedPrekeyPublic = table.Column<byte[]>(type: "bytea", nullable: false),
                    SignedPrekeySignature = table.Column<byte[]>(type: "bytea", nullable: false),
                    SignedPrekeyRotatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DevicePrekeyBundles", x => x.DeviceId);
                    table.ForeignKey(
                        name: "FK_DevicePrekeyBundles_UserDevices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "UserDevices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OneTimePrekeys",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    PrekeyId = table.Column<long>(type: "bigint", nullable: false),
                    PublicKey = table.Column<byte[]>(type: "bytea", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OneTimePrekeys", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OneTimePrekeys_UserDevices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "UserDevices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OneTimePrekeys_DeviceId",
                table: "OneTimePrekeys",
                column: "DeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_OneTimePrekeys_DeviceId_PrekeyId",
                table: "OneTimePrekeys",
                columns: new[] { "DeviceId", "PrekeyId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DevicePrekeyBundles");

            migrationBuilder.DropTable(
                name: "OneTimePrekeys");
        }
    }
}
