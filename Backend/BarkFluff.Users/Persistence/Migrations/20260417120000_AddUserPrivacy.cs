using Microsoft.EntityFrameworkCore.Migrations;

using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace BarkFluff.Users.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUserPrivacy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Privacies",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    ProfileVisibleOnSite = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    AvatarVisibility = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    BioVisibility = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    EmailVisibility = table.Column<int>(type: "integer", nullable: false, defaultValue: 2),
                    SearchVisible = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    OnlineVisibility = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Privacies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Privacies_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Privacies_UserId",
                table: "Privacies",
                column: "UserId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "Privacies");
        }
    }
}
