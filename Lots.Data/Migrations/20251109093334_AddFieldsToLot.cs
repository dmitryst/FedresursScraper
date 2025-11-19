using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lots.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFieldsToLot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AnnouncedAt",
                table: "Lots",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "BankruptMessageId",
                table: "Lots",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "BidAcceptancePeriod",
                table: "Lots",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Platform",
                table: "Lots",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Type",
                table: "Lots",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AnnouncedAt",
                table: "Lots");

            migrationBuilder.DropColumn(
                name: "BankruptMessageId",
                table: "Lots");

            migrationBuilder.DropColumn(
                name: "BidAcceptancePeriod",
                table: "Lots");

            migrationBuilder.DropColumn(
                name: "Platform",
                table: "Lots");

            migrationBuilder.DropColumn(
                name: "Type",
                table: "Lots");
        }
    }
}
