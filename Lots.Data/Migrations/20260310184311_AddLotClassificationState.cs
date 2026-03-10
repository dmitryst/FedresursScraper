using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lots.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddLotClassificationState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Lots_ActiveTradeStatus",
                table: "Lots");

            migrationBuilder.CreateTable(
                name: "LotClassificationStates",
                columns: table => new
                {
                    LotId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LotClassificationStates", x => x.LotId);
                    table.ForeignKey(
                        name: "FK_LotClassificationStates_Lots_LotId",
                        column: x => x.LotId,
                        principalTable: "Lots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Lots_ActiveTradeStatus",
                table: "Lots",
                column: "TradeStatus",
                filter: "\"TradeStatus\" IS NULL OR \"TradeStatus\" = '' OR \"TradeStatus\" NOT IN ('Завершенные', 'Торги отменены', 'Торги не состоялись', 'Торги завершены (нет данных)', 'Аннулированные')");

            migrationBuilder.CreateIndex(
                name: "IX_ClassificationStates_Queue",
                table: "LotClassificationStates",
                columns: new[] { "Status", "NextAttemptAt" },
                filter: "\"Status\" IN (0, 1, 3)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LotClassificationStates");

            migrationBuilder.DropIndex(
                name: "IX_Lots_ActiveTradeStatus",
                table: "Lots");

            migrationBuilder.CreateIndex(
                name: "IX_Lots_ActiveTradeStatus",
                table: "Lots",
                column: "TradeStatus",
                filter: "\"TradeStatus\" IS NULL OR \"TradeStatus\" = '' OR \"TradeStatus\" NOT IN ('Завершенные', 'Торги отменены', 'Торги не состоялись', 'Торги завершены (нет данных)')");
        }
    }
}
