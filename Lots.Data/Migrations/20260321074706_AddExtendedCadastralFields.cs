using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lots.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddExtendedCadastralFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ObjectType",
                table: "CadastralInfo",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OwnershipType",
                table: "CadastralInfo",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RegDate",
                table: "CadastralInfo",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RightType",
                table: "CadastralInfo",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ObjectType",
                table: "CadastralInfo");

            migrationBuilder.DropColumn(
                name: "OwnershipType",
                table: "CadastralInfo");

            migrationBuilder.DropColumn(
                name: "RegDate",
                table: "CadastralInfo");

            migrationBuilder.DropColumn(
                name: "RightType",
                table: "CadastralInfo");
        }
    }
}
