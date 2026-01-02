using Microsoft.EntityFrameworkCore.Migrations;
using NpgsqlTypes;

#nullable disable

namespace Lots.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFullTextSearch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:pg_trgm", ",,")
                .Annotation("Npgsql:PostgresExtension:unaccent", ",,")
                .Annotation("Npgsql:PostgresExtension:uuid-ossp", ",,");

            migrationBuilder.AddColumn<NpgsqlTsVector>(
                name: "SearchVector",
                table: "Lots",
                type: "tsvector",
                nullable: false)
                .Annotation("Npgsql:TsVectorConfig", "russian")
                .Annotation("Npgsql:TsVectorProperties", new[] { "Title", "Description" });

            migrationBuilder.AddColumn<string>(
                name: "CleanCadastralNumber",
                table: "LotCadastralNumbers",
                type: "text",
                nullable: false,
                computedColumnSql: "regexp_replace(\"CadastralNumber\", '\\D', '', 'g')",
                stored: true);

            migrationBuilder.CreateIndex(
                name: "IX_Lots_SearchVector",
                table: "Lots",
                column: "SearchVector")
                .Annotation("Npgsql:IndexMethod", "GIN");

            migrationBuilder.CreateIndex(
                name: "IX_LotCadastralNumbers_CleanCadastralNumber",
                table: "LotCadastralNumbers",
                column: "CleanCadastralNumber");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Lots_SearchVector",
                table: "Lots");

            migrationBuilder.DropIndex(
                name: "IX_LotCadastralNumbers_CleanCadastralNumber",
                table: "LotCadastralNumbers");

            migrationBuilder.DropColumn(
                name: "CleanCadastralNumber",
                table: "LotCadastralNumbers");

            migrationBuilder.DropColumn(
                name: "SearchVector",
                table: "Lots");

            migrationBuilder.AlterDatabase()
                .OldAnnotation("Npgsql:PostgresExtension:pg_trgm", ",,")
                .OldAnnotation("Npgsql:PostgresExtension:unaccent", ",,")
                .OldAnnotation("Npgsql:PostgresExtension:uuid-ossp", ",,");
        }
    }
}
