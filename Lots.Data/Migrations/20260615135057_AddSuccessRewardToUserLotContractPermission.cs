using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lots.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSuccessRewardToUserLotContractPermission : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UserLotContractPermissions_UserId",
                table: "UserLotContractPermissions");

            migrationBuilder.AddColumn<decimal>(
                name: "FixedRewardAmount",
                table: "UserLotContractPermissions",
                type: "numeric",
                nullable: false,
                defaultValue: 5000m);

            migrationBuilder.AddColumn<decimal>(
                name: "SuccessRewardAmount",
                table: "UserLotContractPermissions",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateIndex(
                name: "IX_UserLotContractPermissions_UserId_LotId",
                table: "UserLotContractPermissions",
                columns: new[] { "UserId", "LotId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UserLotContractPermissions_UserId_LotId",
                table: "UserLotContractPermissions");

            migrationBuilder.DropColumn(
                name: "FixedRewardAmount",
                table: "UserLotContractPermissions");

            migrationBuilder.DropColumn(
                name: "SuccessRewardAmount",
                table: "UserLotContractPermissions");

            migrationBuilder.CreateIndex(
                name: "IX_UserLotContractPermissions_UserId",
                table: "UserLotContractPermissions",
                column: "UserId");
        }
    }
}
