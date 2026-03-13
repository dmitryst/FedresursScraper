using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lots.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBiddingAndOwnershipToAlerts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BiddingType",
                table: "LotAlerts",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsSharedOwnership",
                table: "LotAlerts",
                type: "boolean",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BiddingType",
                table: "LotAlerts");

            migrationBuilder.DropColumn(
                name: "IsSharedOwnership",
                table: "LotAlerts");
        }
    }
}
