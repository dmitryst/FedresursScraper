using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lots.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddLotPublicId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence<int>(
                name: "lots_public_id_seq",
                startValue: 10001L);

            migrationBuilder.AddColumn<int>(
                name: "PublicId",
                table: "Lots",
                type: "integer",
                nullable: false,
                defaultValueSql: "nextval('lots_public_id_seq')");

            migrationBuilder.CreateIndex(
                name: "IX_Lots_PublicId",
                table: "Lots",
                column: "PublicId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Lots_PublicId",
                table: "Lots");

            migrationBuilder.DropColumn(
                name: "PublicId",
                table: "Lots");

            migrationBuilder.DropSequence(
                name: "lots_public_id_seq");
        }
    }
}
