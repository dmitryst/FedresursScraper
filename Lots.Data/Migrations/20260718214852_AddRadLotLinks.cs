using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lots.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRadLotLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RadLotLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EfrsbLotId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EfrsbLotIdNormalized = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    LotNumber = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    LotNumberNormalized = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ProductId = table.Column<long>(type: "bigint", nullable: false),
                    LotCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    LotUrl = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    Status = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RadLotLinks", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RadLotLinks_EfrsbLotNormalized",
                table: "RadLotLinks",
                columns: new[] { "EfrsbLotIdNormalized", "LotNumberNormalized" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RadLotLinks_ProductId",
                table: "RadLotLinks",
                column: "ProductId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RadLotLinks_UpdatedAt",
                table: "RadLotLinks",
                column: "UpdatedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RadLotLinks");
        }
    }
}
