using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarkFluff.Settings.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HistoryDescendingIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SettingsHistory_SettingsTable_Key_ChangedAt_Id",
                table: "SettingsHistory");

            migrationBuilder.CreateIndex(
                name: "IX_SettingsHistory_SettingsTable_Key_ChangedAt_Id",
                table: "SettingsHistory",
                columns: new[] { "SettingsTable", "Key", "ChangedAt", "Id" },
                descending: new[] { false, false, true, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SettingsHistory_SettingsTable_Key_ChangedAt_Id",
                table: "SettingsHistory");

            migrationBuilder.CreateIndex(
                name: "IX_SettingsHistory_SettingsTable_Key_ChangedAt_Id",
                table: "SettingsHistory",
                columns: new[] { "SettingsTable", "Key", "ChangedAt", "Id" });
        }
    }
}
