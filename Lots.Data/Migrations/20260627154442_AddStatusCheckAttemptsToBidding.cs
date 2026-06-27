using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lots.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddStatusCheckAttemptsToBidding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "StatusCheckAttempts",
                table: "Biddings",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "StatusCheckAttempts",
                table: "Biddings");
        }
    }
}
