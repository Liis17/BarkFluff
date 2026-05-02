using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace BarkFluff.Messages.Migrations
{
    /// <inheritdoc />
    public partial class AddForwardedMessageAttachment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "FileId",
                table: "MessageAttachments",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddColumn<string>(
                name: "ForwardedAuthorName",
                table: "MessageAttachments",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ForwardedOriginalMessageId",
                table: "MessageAttachments",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ForwardedText",
                table: "MessageAttachments",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ForwardedMessageAttachment",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    FileId = table.Column<string>(type: "text", nullable: false),
                    PreviewUrl = table.Column<string>(type: "text", nullable: true),
                    FileSize = table.Column<long>(type: "bigint", nullable: false),
                    MessageAttachmentId = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ForwardedMessageAttachment", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ForwardedMessageAttachment_MessageAttachments_MessageAttach~",
                        column: x => x.MessageAttachmentId,
                        principalTable: "MessageAttachments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ForwardedMessageAttachment_MessageAttachmentId",
                table: "ForwardedMessageAttachment",
                column: "MessageAttachmentId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ForwardedMessageAttachment");

            migrationBuilder.DropColumn(
                name: "ForwardedAuthorName",
                table: "MessageAttachments");

            migrationBuilder.DropColumn(
                name: "ForwardedOriginalMessageId",
                table: "MessageAttachments");

            migrationBuilder.DropColumn(
                name: "ForwardedText",
                table: "MessageAttachments");

            migrationBuilder.AlterColumn<string>(
                name: "FileId",
                table: "MessageAttachments",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);
        }
    }
}
