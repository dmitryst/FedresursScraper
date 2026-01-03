using Microsoft.EntityFrameworkCore.Migrations;
using NpgsqlTypes;

#nullable disable

namespace Lots.Data.Migrations
{
    /// <inheritdoc />
    public partial class UseHunspellDictionary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // я пытался выполнить здесь скрипт через migrationBuilder.Sql()
            // но не получилось, пришлось выполнить скрипт init-search-config.sql вручную

            // Команда AlterColumn для GENERATED колонки в Postgres обычно автоматически
            // вызывает пересчет значений, так что вручную делать UPDATE не нужно
            migrationBuilder.AlterColumn<NpgsqlTsVector>(
                name: "SearchVector",
                table: "Lots",
                type: "tsvector",
                nullable: false,
                oldClrType: typeof(NpgsqlTsVector),
                oldType: "tsvector")
                .Annotation("Npgsql:TsVectorConfig", "russian_h")
                .Annotation("Npgsql:TsVectorProperties", new[] { "Title", "Description" })
                .OldAnnotation("Npgsql:TsVectorConfig", "russian")
                .OldAnnotation("Npgsql:TsVectorProperties", new[] { "Title", "Description" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<NpgsqlTsVector>(
                name: "SearchVector",
                table: "Lots",
                type: "tsvector",
                nullable: false,
                oldClrType: typeof(NpgsqlTsVector),
                oldType: "tsvector")
                .Annotation("Npgsql:TsVectorConfig", "russian")
                .Annotation("Npgsql:TsVectorProperties", new[] { "Title", "Description" })
                .OldAnnotation("Npgsql:TsVectorConfig", "russian_h")
                .OldAnnotation("Npgsql:TsVectorProperties", new[] { "Title", "Description" });

            // Удаляем словарь и конфигурацию
            migrationBuilder.Sql(@"
                DROP TEXT SEARCH CONFIGURATION IF EXISTS public.russian_h;
                DROP TEXT SEARCH DICTIONARY IF EXISTS public.russian_hunspell;
            ");
        }
    }
}
