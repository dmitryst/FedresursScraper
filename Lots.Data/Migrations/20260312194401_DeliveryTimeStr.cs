using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lots.Data.Migrations
{
    /// <inheritdoc />
    public partial class DeliveryTimeStr : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DeliveryTimeStr",
                table: "LotAlerts",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DeliveryTimeStr",
                table: "LotAlerts");
        }
    }
}
