using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace BarkFluff.Configuration.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddConfigurationRevisions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ConfigurationRevisions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ConfigurationItemId = table.Column<long>(type: "bigint", nullable: false),
                    Section = table.Column<string>(type: "text", nullable: false),
                    Key = table.Column<string>(type: "text", nullable: false),
                    ServiceId = table.Column<int>(type: "integer", nullable: false),
                    PreviousValue = table.Column<string>(type: "text", nullable: false),
                    NewValue = table.Column<string>(type: "text", nullable: false),
                    ChangedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ChangedBy = table.Column<string>(type: "text", nullable: false),
                    ChangedFrom = table.Column<string>(type: "text", nullable: false),
                    ChangeKind = table.Column<string>(type: "text", nullable: false),
                    SourceRevisionId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConfigurationRevisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ConfigurationRevisions_Configurations_ConfigurationItemId",
                        column: x => x.ConfigurationItemId,
                        principalTable: "Configurations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ConfigurationRevisions_ConfigurationItemId",
                table: "ConfigurationRevisions",
                column: "ConfigurationItemId");

            migrationBuilder.CreateIndex(
                name: "IX_ConfigurationRevisions_ServiceId_Section_Key_ChangedAt",
                table: "ConfigurationRevisions",
                columns: new[] { "ServiceId", "Section", "Key", "ChangedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConfigurationRevisions");
        }
    }
}
