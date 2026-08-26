using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace BarkFluff.Messages.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMessageIdempotencyOutbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ClientOperationId",
                table: "Messages",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MessageOutbox",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    MessageId = table.Column<long>(type: "bigint", nullable: false),
                    Payload = table.Column<byte[]>(type: "bytea", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    LastError = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MessageOutbox", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Messages_SenderId_ClientOperationId",
                table: "Messages",
                columns: new[] { "SenderId", "ClientOperationId" },
                unique: true,
                filter: "\"SenderId\" IS NOT NULL AND \"ClientOperationId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_MessageOutbox_EventId",
                table: "MessageOutbox",
                column: "EventId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MessageOutbox_MessageId",
                table: "MessageOutbox",
                column: "MessageId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MessageOutbox_Status_NextAttemptAt",
                table: "MessageOutbox",
                columns: new[] { "Status", "NextAttemptAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MessageOutbox");

            migrationBuilder.DropIndex(
                name: "IX_Messages_SenderId_ClientOperationId",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "ClientOperationId",
                table: "Messages");
        }
    }
}
