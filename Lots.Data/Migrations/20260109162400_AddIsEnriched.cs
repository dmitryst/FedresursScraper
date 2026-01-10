using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lots.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddIsEnriched : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "EnrichedAt",
                table: "Biddings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsEnriched",
                table: "Biddings",
                type: "boolean",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "LotImages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LotId = table.Column<Guid>(type: "uuid", nullable: false),
                    Url = table.Column<string>(type: "text", nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LotImages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LotImages_Lots_LotId",
                        column: x => x.LotId,
                        principalTable: "Lots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LotPriceSchedules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LotId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Price = table.Column<decimal>(type: "numeric", nullable: false),
                    Deposit = table.Column<decimal>(type: "numeric", nullable: false),
                    EstimatedRank = table.Column<double>(type: "double precision", nullable: true),
                    PotentialRoi = table.Column<double>(type: "double precision", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LotPriceSchedules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LotPriceSchedules_Lots_LotId",
                        column: x => x.LotId,
                        principalTable: "Lots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LotImages_LotId",
                table: "LotImages",
                column: "LotId");

            migrationBuilder.CreateIndex(
                name: "IX_LotPriceSchedules_LotId",
                table: "LotPriceSchedules",
                column: "LotId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LotImages");

            migrationBuilder.DropTable(
                name: "LotPriceSchedules");

            migrationBuilder.DropColumn(
                name: "EnrichedAt",
                table: "Biddings");

            migrationBuilder.DropColumn(
                name: "IsEnriched",
                table: "Biddings");
        }
    }
}
