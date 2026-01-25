using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lots.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPropertyLocation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PropertyFullAddress",
                table: "Lots",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PropertyRegionCode",
                table: "Lots",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PropertyRegionName",
                table: "Lots",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PropertyFullAddress",
                table: "Lots");

            migrationBuilder.DropColumn(
                name: "PropertyRegionCode",
                table: "Lots");

            migrationBuilder.DropColumn(
                name: "PropertyRegionName",
                table: "Lots");
        }
    }
}
