using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lots.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPerformanceIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // GIN index for Attributes (jsonb)
            migrationBuilder.Sql(@"CREATE INDEX ""IX_Lots_Attributes_GIN"" ON ""Lots"" USING GIN (""Attributes"");");

            // Partial index for NeedsDescriptionReview
            migrationBuilder.Sql(@"
                CREATE INDEX ""IX_Lots_NeedsDescriptionReview"" 
                ON ""Lots"" (""NeedsDescriptionReview"") 
                WHERE ""NeedsDescriptionReview"" = TRUE 
                AND (""TradeStatus"" IS NULL OR ""TradeStatus"" = '' OR ""TradeStatus"" NOT IN ('Завершенные', 'Торги отменены', 'Торги не состоялись', 'Торги завершены (нет данных)', 'Аннулированные'));
            ");

            // Expression index for brand and model case-insensitive search
            migrationBuilder.Sql(@"
                CREATE INDEX ""IX_Lots_Attributes_Brand_Lower"" 
                ON ""Lots"" ((LOWER(jsonb_extract_path_text(""Attributes"", 'brand'))));
            ");

            migrationBuilder.Sql(@"
                CREATE INDEX ""IX_Lots_Attributes_Model_Lower"" 
                ON ""Lots"" ((LOWER(jsonb_extract_path_text(""Attributes"", 'model'))));
            ");

            // Index for fast sorting by CreatedAt
            migrationBuilder.Sql(@"CREATE INDEX ""IX_Lots_CreatedAt"" ON ""Lots"" (""CreatedAt"" DESC);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_Lots_CreatedAt"";");
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_Lots_Attributes_Model_Lower"";");
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_Lots_Attributes_Brand_Lower"";");
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_Lots_NeedsDescriptionReview"";");
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_Lots_Attributes_GIN"";");
        }
    }
}
