using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lots.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddLotTradeStatusFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "FinalPrice",
                table: "Lots",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TradeStatus",
                table: "Lots",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WinnerInn",
                table: "Lots",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WinnerName",
                table: "Lots",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FinalPrice",
                table: "Lots");

            migrationBuilder.DropColumn(
                name: "TradeStatus",
                table: "Lots");

            migrationBuilder.DropColumn(
                name: "WinnerInn",
                table: "Lots");

            migrationBuilder.DropColumn(
                name: "WinnerName",
                table: "Lots");
        }
    }
}
