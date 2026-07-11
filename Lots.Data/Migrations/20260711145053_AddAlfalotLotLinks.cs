using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lots.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAlfalotLotLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AlfalotLotLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TradeNumber = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TradeNumberNormalized = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    LotNumber = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    LotNumberNormalized = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TradeUrl = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    LotUrl = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    Status = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ApplicationsEndAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EventAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AlfalotLotLinks", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AlfalotLotLinks_TradeLotNormalized",
                table: "AlfalotLotLinks",
                columns: new[] { "TradeNumberNormalized", "LotNumberNormalized" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AlfalotLotLinks_UpdatedAt",
                table: "AlfalotLotLinks",
                column: "UpdatedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AlfalotLotLinks");
        }
    }
}
