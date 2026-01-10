using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lots.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddEnrichmentState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EnrichmentStates",
                columns: table => new
                {
                    BiddingId = table.Column<Guid>(type: "uuid", nullable: false),
                    RetryCount = table.Column<int>(type: "integer", nullable: false),
                    LastAttemptAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EnrichmentStates", x => x.BiddingId);
                    table.ForeignKey(
                        name: "FK_EnrichmentStates_Biddings_BiddingId",
                        column: x => x.BiddingId,
                        principalTable: "Biddings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EnrichmentStates");
        }
    }
}
