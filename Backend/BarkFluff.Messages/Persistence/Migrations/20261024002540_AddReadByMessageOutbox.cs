using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarkFluff.Messages.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReadByMessageOutbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MessageOutbox_MessageId",
                table: "MessageOutbox");

            migrationBuilder.AddColumn<int>(
                name: "EventKind",
                table: "MessageOutbox",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_MessageOutbox_MessageId",
                table: "MessageOutbox",
                column: "MessageId",
                unique: true,
                filter: "\"EventKind\" = 0");

            migrationBuilder.CreateIndex(
                name: "IX_MessageOutbox_Status_EventKind_Id_NextAttemptAt",
                table: "MessageOutbox",
                columns: new[] { "Status", "EventKind", "Id", "NextAttemptAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MessageOutbox_MessageId",
                table: "MessageOutbox");

            migrationBuilder.DropIndex(
                name: "IX_MessageOutbox_Status_EventKind_Id_NextAttemptAt",
                table: "MessageOutbox");

            migrationBuilder.DropColumn(
                name: "EventKind",
                table: "MessageOutbox");

            migrationBuilder.CreateIndex(
                name: "IX_MessageOutbox_MessageId",
                table: "MessageOutbox",
                column: "MessageId",
                unique: true);
        }
    }
}
