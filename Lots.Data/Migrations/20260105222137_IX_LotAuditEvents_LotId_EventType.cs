using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lots.Data.Migrations
{
    /// <inheritdoc />
    public partial class IX_LotAuditEvents_LotId_EventType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_LotAuditEvents_LotId_EventType",
                table: "LotAuditEvents",
                columns: new[] { "LotId", "EventType" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_LotAuditEvents_LotId_EventType",
                table: "LotAuditEvents");
        }
    }
}
