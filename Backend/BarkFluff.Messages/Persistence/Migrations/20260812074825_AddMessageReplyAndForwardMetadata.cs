using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarkFluff.Messages.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMessageReplyAndForwardMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "ReplyToMessageId",
                table: "Messages",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ForwardedOrder",
                table: "MessageAttachments",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ForwardedOriginalChatId",
                table: "MessageAttachments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ForwardedOriginalSenderId",
                table: "MessageAttachments",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ForwardedOriginalSentAt",
                table: "MessageAttachments",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReplyToMessageId",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "ForwardedOrder",
                table: "MessageAttachments");

            migrationBuilder.DropColumn(
                name: "ForwardedOriginalChatId",
                table: "MessageAttachments");

            migrationBuilder.DropColumn(
                name: "ForwardedOriginalSenderId",
                table: "MessageAttachments");

            migrationBuilder.DropColumn(
                name: "ForwardedOriginalSentAt",
                table: "MessageAttachments");
        }
    }
}
