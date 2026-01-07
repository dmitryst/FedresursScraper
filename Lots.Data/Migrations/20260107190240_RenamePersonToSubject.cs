using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lots.Data.Migrations
{
    /// <inheritdoc />
    public partial class RenamePersonToSubject : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Biddings_Persons_ArbitrationManagerId",
                table: "Biddings");

            migrationBuilder.DropForeignKey(
                name: "FK_Biddings_Persons_DebtorId",
                table: "Biddings");

            migrationBuilder.DropTable(
                name: "Persons");

            migrationBuilder.CreateTable(
                name: "Subjects",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Inn = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Snils = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Ogrn = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Type = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Subjects", x => x.Id);
                });

            migrationBuilder.AddForeignKey(
                name: "FK_Biddings_Subjects_ArbitrationManagerId",
                table: "Biddings",
                column: "ArbitrationManagerId",
                principalTable: "Subjects",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Biddings_Subjects_DebtorId",
                table: "Biddings",
                column: "DebtorId",
                principalTable: "Subjects",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Biddings_Subjects_ArbitrationManagerId",
                table: "Biddings");

            migrationBuilder.DropForeignKey(
                name: "FK_Biddings_Subjects_DebtorId",
                table: "Biddings");

            migrationBuilder.DropTable(
                name: "Subjects");

            migrationBuilder.CreateTable(
                name: "Persons",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Inn = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Snils = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Persons", x => x.Id);
                });

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
    }
}
