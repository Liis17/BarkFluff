using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarkFluff.Federation.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOutboxTraceContext : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CorrelationId",
                table: "FederationOutbox",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TraceParent",
                table: "FederationOutbox",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TraceState",
                table: "FederationOutbox",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CorrelationId",
                table: "FederationOutbox");

            migrationBuilder.DropColumn(
                name: "TraceParent",
                table: "FederationOutbox");

            migrationBuilder.DropColumn(
                name: "TraceState",
                table: "FederationOutbox");
        }
    }
}
