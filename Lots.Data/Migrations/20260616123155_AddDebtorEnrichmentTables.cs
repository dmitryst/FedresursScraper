using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lots.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDebtorEnrichmentTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DebtLotProfiles_Queue",
                table: "DebtLotProfiles");

            migrationBuilder.CreateTable(
                name: "DebtorEnrichmentProfiles",
                columns: table => new
                {
                    LotId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubjectId = table.Column<Guid>(type: "uuid", nullable: true),
                    DebtorType = table.Column<int>(type: "integer", nullable: false),
                    ResolvedName = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IsResolvedNameEncrypted = table.Column<bool>(type: "boolean", nullable: false),
                    ResolvedInn = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    IsResolvedInnEncrypted = table.Column<bool>(type: "boolean", nullable: false),
                    ResolvedSnils = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    IsResolvedSnilsEncrypted = table.Column<bool>(type: "boolean", nullable: false),
                    FnsStepStatus = table.Column<int>(type: "integer", nullable: false),
                    BankruptcyStepStatus = table.Column<int>(type: "integer", nullable: false),
                    KadStepStatus = table.Column<int>(type: "integer", nullable: false),
                    FsspStepStatus = table.Column<int>(type: "integer", nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DebtorEnrichmentProfiles", x => x.LotId);
                    table.ForeignKey(
                        name: "FK_DebtorEnrichmentProfiles_DebtLotProfiles_LotId",
                        column: x => x.LotId,
                        principalTable: "DebtLotProfiles",
                        principalColumn: "LotId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DebtorEnrichmentProfiles_Subjects_SubjectId",
                        column: x => x.SubjectId,
                        principalTable: "Subjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "DebtorBankruptcyChecks",
                columns: table => new
                {
                    LotId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsInBankruptcy = table.Column<bool>(type: "boolean", nullable: false),
                    BankruptcyCaseNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    StatusText = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IsStopFactor = table.Column<bool>(type: "boolean", nullable: false),
                    RawResponse = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    FetchedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DebtorBankruptcyChecks", x => x.LotId);
                    table.ForeignKey(
                        name: "FK_DebtorBankruptcyChecks_DebtorEnrichmentProfiles_LotId",
                        column: x => x.LotId,
                        principalTable: "DebtorEnrichmentProfiles",
                        principalColumn: "LotId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DebtorFnsSnapshots",
                columns: table => new
                {
                    LotId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyStatus = table.Column<int>(type: "integer", nullable: false),
                    CompanyStatusText = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IsStopFactor = table.Column<bool>(type: "boolean", nullable: false),
                    Revenue = table.Column<decimal>(type: "numeric", nullable: true),
                    NetAssets = table.Column<decimal>(type: "numeric", nullable: true),
                    RawResponse = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    FetchedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DebtorFnsSnapshots", x => x.LotId);
                    table.ForeignKey(
                        name: "FK_DebtorFnsSnapshots_DebtorEnrichmentProfiles_LotId",
                        column: x => x.LotId,
                        principalTable: "DebtorEnrichmentProfiles",
                        principalColumn: "LotId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DebtorFsspRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LotId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProceedingNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    DebtAmount = table.Column<decimal>(type: "numeric", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ClosedUnderArticle46 = table.Column<bool>(type: "boolean", nullable: false),
                    IsStopFactor = table.Column<bool>(type: "boolean", nullable: false),
                    Details = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    FetchedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DebtorFsspRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DebtorFsspRecords_DebtorEnrichmentProfiles_LotId",
                        column: x => x.LotId,
                        principalTable: "DebtorEnrichmentProfiles",
                        principalColumn: "LotId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DebtorKadCaseSnapshots",
                columns: table => new
                {
                    LotId = table.Column<Guid>(type: "uuid", nullable: false),
                    CaseNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    CaseSubject = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    DisputeCategory = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CourtName = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    LastActDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DocumentsJson = table.Column<string>(type: "text", nullable: true),
                    RawResponse = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    FetchedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DebtorKadCaseSnapshots", x => x.LotId);
                    table.ForeignKey(
                        name: "FK_DebtorKadCaseSnapshots_DebtorEnrichmentProfiles_LotId",
                        column: x => x.LotId,
                        principalTable: "DebtorEnrichmentProfiles",
                        principalColumn: "LotId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DebtLotProfiles_EnrichmentQueue",
                table: "DebtLotProfiles",
                columns: new[] { "Status", "NextAttemptAt" },
                filter: "\"Status\" IN (2, 3, 6)");

            migrationBuilder.CreateIndex(
                name: "IX_DebtorEnrichmentProfiles_SubjectId",
                table: "DebtorEnrichmentProfiles",
                column: "SubjectId");

            migrationBuilder.CreateIndex(
                name: "IX_DebtorFsspRecords_LotId",
                table: "DebtorFsspRecords",
                column: "LotId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DebtorBankruptcyChecks");

            migrationBuilder.DropTable(
                name: "DebtorFnsSnapshots");

            migrationBuilder.DropTable(
                name: "DebtorFsspRecords");

            migrationBuilder.DropTable(
                name: "DebtorKadCaseSnapshots");

            migrationBuilder.DropTable(
                name: "DebtorEnrichmentProfiles");

            migrationBuilder.DropIndex(
                name: "IX_DebtLotProfiles_EnrichmentQueue",
                table: "DebtLotProfiles");

            migrationBuilder.CreateIndex(
                name: "IX_DebtLotProfiles_Queue",
                table: "DebtLotProfiles",
                columns: new[] { "Status", "NextAttemptAt" },
                filter: "\"Status\" IN (0, 1, 3, 5)");
        }
    }
}
