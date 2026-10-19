using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarkFluff.Messages.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOutboxTraceContext : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CorrelationId",
                table: "MessageOutbox",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TraceParent",
                table: "MessageOutbox",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TraceState",
                table: "MessageOutbox",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CorrelationId",
                table: "MessageOutbox");

            migrationBuilder.DropColumn(
                name: "TraceParent",
                table: "MessageOutbox");

            migrationBuilder.DropColumn(
                name: "TraceState",
                table: "MessageOutbox");
        }
    }
}
