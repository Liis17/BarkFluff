using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace BarkFluff.Users.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPqxdhPrekeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ConsumedAt",
                table: "OneTimePrekeys",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "LastResortKyberPrekeyId",
                table: "DevicePrekeyBundles",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "LastResortKyberPrekeyPublic",
                table: "DevicePrekeyBundles",
                type: "bytea",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "LastResortKyberPrekeySignature",
                table: "DevicePrekeyBundles",
                type: "bytea",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "OneTimeKyberPrekeys",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    PrekeyId = table.Column<long>(type: "bigint", nullable: false),
                    PublicKey = table.Column<byte[]>(type: "bytea", nullable: false),
                    Signature = table.Column<byte[]>(type: "bytea", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ConsumedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OneTimeKyberPrekeys", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OneTimeKyberPrekeys_UserDevices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "UserDevices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OneTimeKyberPrekeys_DeviceId",
                table: "OneTimeKyberPrekeys",
                column: "DeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_OneTimeKyberPrekeys_DeviceId_PrekeyId",
                table: "OneTimeKyberPrekeys",
                columns: new[] { "DeviceId", "PrekeyId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OneTimeKyberPrekeys");

            migrationBuilder.DropColumn(
                name: "ConsumedAt",
                table: "OneTimePrekeys");

            migrationBuilder.DropColumn(
                name: "LastResortKyberPrekeyId",
                table: "DevicePrekeyBundles");

            migrationBuilder.DropColumn(
                name: "LastResortKyberPrekeyPublic",
                table: "DevicePrekeyBundles");

            migrationBuilder.DropColumn(
                name: "LastResortKyberPrekeySignature",
                table: "DevicePrekeyBundles");
        }
    }
}
