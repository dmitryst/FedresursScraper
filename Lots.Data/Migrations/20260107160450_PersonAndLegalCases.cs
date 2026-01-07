using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lots.Data.Migrations
{
    /// <inheritdoc />
    public partial class PersonAndLegalCases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ArbitrationManagerId",
                table: "Biddings",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DebtorId",
                table: "Biddings",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LegalCaseId",
                table: "Biddings",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Organizer",
                table: "Biddings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TradePeriod",
                table: "Biddings",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "LegalCases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CaseNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LegalCases", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Persons",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Inn = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Snils = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Persons", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Biddings_ArbitrationManagerId",
                table: "Biddings",
                column: "ArbitrationManagerId");

            migrationBuilder.CreateIndex(
                name: "IX_Biddings_DebtorId",
                table: "Biddings",
                column: "DebtorId");

            migrationBuilder.CreateIndex(
                name: "IX_Biddings_LegalCaseId",
                table: "Biddings",
                column: "LegalCaseId");

            migrationBuilder.AddForeignKey(
                name: "FK_Biddings_LegalCases_LegalCaseId",
                table: "Biddings",
                column: "LegalCaseId",
                principalTable: "LegalCases",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Biddings_Persons_ArbitrationManagerId",
                table: "Biddings",
                column: "ArbitrationManagerId",
                principalTable: "Persons",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Biddings_Persons_DebtorId",
                table: "Biddings",
                column: "DebtorId",
                principalTable: "Persons",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Biddings_LegalCases_LegalCaseId",
                table: "Biddings");

            migrationBuilder.DropForeignKey(
                name: "FK_Biddings_Persons_ArbitrationManagerId",
                table: "Biddings");

            migrationBuilder.DropForeignKey(
                name: "FK_Biddings_Persons_DebtorId",
                table: "Biddings");

            migrationBuilder.DropTable(
                name: "LegalCases");

            migrationBuilder.DropTable(
                name: "Persons");

            migrationBuilder.DropIndex(
                name: "IX_Biddings_ArbitrationManagerId",
                table: "Biddings");

            migrationBuilder.DropIndex(
                name: "IX_Biddings_DebtorId",
                table: "Biddings");

            migrationBuilder.DropIndex(
                name: "IX_Biddings_LegalCaseId",
                table: "Biddings");

            migrationBuilder.DropColumn(
                name: "ArbitrationManagerId",
                table: "Biddings");

            migrationBuilder.DropColumn(
                name: "DebtorId",
                table: "Biddings");

            migrationBuilder.DropColumn(
                name: "LegalCaseId",
                table: "Biddings");

            migrationBuilder.DropColumn(
                name: "Organizer",
                table: "Biddings");

            migrationBuilder.DropColumn(
                name: "TradePeriod",
                table: "Biddings");
        }
    }
}
