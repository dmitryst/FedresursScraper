using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lots.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDebtScoringTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DebtLotProfiles",
                columns: table => new
                {
                    LotId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    DebtNominal = table.Column<decimal>(type: "numeric", nullable: true),
                    DebtBasis = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CaseNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    RejectionReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DebtLotProfiles", x => x.LotId);
                    table.ForeignKey(
                        name: "FK_DebtLotProfiles_Lots_LotId",
                        column: x => x.LotId,
                        principalTable: "Lots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DebtCourtDocuments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LotId = table.Column<Guid>(type: "uuid", nullable: false),
                    LotDocumentId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceUrl = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Extension = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    DocumentType = table.Column<int>(type: "integer", nullable: false),
                    ProcessingStatus = table.Column<int>(type: "integer", nullable: false),
                    OcrText = table.Column<string>(type: "text", nullable: true),
                    OcrConfidence = table.Column<double>(type: "double precision", nullable: true),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    LastError = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ProcessedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DebtCourtDocuments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DebtCourtDocuments_DebtLotProfiles_LotId",
                        column: x => x.LotId,
                        principalTable: "DebtLotProfiles",
                        principalColumn: "LotId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DebtCourtDocuments_Documents_LotDocumentId",
                        column: x => x.LotDocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "DebtExtractedEntities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LotId = table.Column<Guid>(type: "uuid", nullable: false),
                    CourtDocumentId = table.Column<Guid>(type: "uuid", nullable: true),
                    EntityType = table.Column<int>(type: "integer", nullable: false),
                    Value = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    IsEncrypted = table.Column<bool>(type: "boolean", nullable: false),
                    Confidence = table.Column<double>(type: "double precision", nullable: false),
                    Source = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DebtExtractedEntities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DebtExtractedEntities_DebtCourtDocuments_CourtDocumentId",
                        column: x => x.CourtDocumentId,
                        principalTable: "DebtCourtDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_DebtExtractedEntities_DebtLotProfiles_LotId",
                        column: x => x.LotId,
                        principalTable: "DebtLotProfiles",
                        principalColumn: "LotId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DebtCourtDocuments_LotDocumentId",
                table: "DebtCourtDocuments",
                column: "LotDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_DebtCourtDocuments_LotId_SourceUrl",
                table: "DebtCourtDocuments",
                columns: new[] { "LotId", "SourceUrl" });

            migrationBuilder.CreateIndex(
                name: "IX_DebtExtractedEntities_CourtDocumentId",
                table: "DebtExtractedEntities",
                column: "CourtDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_DebtExtractedEntities_LotId_EntityType",
                table: "DebtExtractedEntities",
                columns: new[] { "LotId", "EntityType" });

            migrationBuilder.CreateIndex(
                name: "IX_DebtLotProfiles_Queue",
                table: "DebtLotProfiles",
                columns: new[] { "Status", "NextAttemptAt" },
                filter: "\"Status\" IN (0, 1, 3, 5)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DebtExtractedEntities");

            migrationBuilder.DropTable(
                name: "DebtCourtDocuments");

            migrationBuilder.DropTable(
                name: "DebtLotProfiles");
        }
    }
}
