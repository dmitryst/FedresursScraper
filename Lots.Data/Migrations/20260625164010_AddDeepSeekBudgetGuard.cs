using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Lots.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDeepSeekBudgetGuard : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DeepSeekBudgetStates",
                columns: table => new
                {
                    PeriodKey = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    RequestCount = table.Column<long>(type: "bigint", nullable: false),
                    TokenCount = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeepSeekBudgetStates", x => x.PeriodKey);
                });

            migrationBuilder.CreateTable(
                name: "DeepSeekCircuitBreakers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OpenUntil = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Reason = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeepSeekCircuitBreakers", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "DeepSeekCircuitBreakers",
                columns: new[] { "Id", "OpenUntil", "Reason", "UpdatedAt" },
                values: new object[] { 1, null, null, new DateTime(2026, 6, 25, 0, 0, 0, 0, DateTimeKind.Utc) });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DeepSeekBudgetStates");

            migrationBuilder.DropTable(
                name: "DeepSeekCircuitBreakers");
        }
    }
}
