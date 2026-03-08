using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarkFluff.Configuration.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddServerPropsLocation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var now = DateTime.UtcNow;

            migrationBuilder.InsertData(
                table: "Configurations",
                columns: new[] { "Section", "Key", "Value", "EditedAt", "EditedBy", "EditedFrom", "ServiceId" },
                values: new object[] { "ServerProps", "Location", "", now, "System", "Migration", 3 });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "Configurations",
                keyColumns: new[] { "Section", "Key", "ServiceId" },
                keyValues: new object[] { "ServerProps", "Location", 3 });
        }
    }
}
