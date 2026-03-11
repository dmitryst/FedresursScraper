using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lots.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddLotAlerts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LotAlerts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RegionCodes = table.Column<string[]>(type: "text[]", nullable: true),
                    Categories = table.Column<string[]>(type: "text[]", nullable: true),
                    MinPrice = table.Column<decimal>(type: "numeric", nullable: true),
                    MaxPrice = table.Column<decimal>(type: "numeric", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LotAlerts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LotAlerts_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LotAlertMatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LotAlertId = table.Column<Guid>(type: "uuid", nullable: false),
                    LotId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsSent = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LotAlertMatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LotAlertMatches_LotAlerts_LotAlertId",
                        column: x => x.LotAlertId,
                        principalTable: "LotAlerts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LotAlertMatches_Lots_LotId",
                        column: x => x.LotId,
                        principalTable: "Lots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LotAlertMatches_LotAlertId",
                table: "LotAlertMatches",
                column: "LotAlertId");

            migrationBuilder.CreateIndex(
                name: "IX_LotAlertMatches_LotId",
                table: "LotAlertMatches",
                column: "LotId");

            migrationBuilder.CreateIndex(
                name: "IX_LotAlertMatches_Unsent",
                table: "LotAlertMatches",
                column: "IsSent",
                filter: "\"IsSent\" = FALSE");

            migrationBuilder.CreateIndex(
                name: "IX_LotAlerts_UserId",
                table: "LotAlerts",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LotAlertMatches");

            migrationBuilder.DropTable(
                name: "LotAlerts");
        }
    }
}
