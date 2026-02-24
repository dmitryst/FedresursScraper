using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lots.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddActiveLotPartialIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Lots_ActiveTradeStatus",
                table: "Lots",
                column: "TradeStatus",
                filter: "\"TradeStatus\" IS NULL OR \"TradeStatus\" = '' OR \"TradeStatus\" NOT IN ('Завершенные', 'Торги отменены', 'Торги не состоялись', 'Торги завершены (нет данных)')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Lots_ActiveTradeStatus",
                table: "Lots");
        }
    }
}
