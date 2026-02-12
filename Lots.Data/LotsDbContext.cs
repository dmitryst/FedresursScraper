using Lots.Data.Entities;
using Microsoft.EntityFrameworkCore;

public class LotsDbContext : DbContext
{
    public LotsDbContext(DbContextOptions<LotsDbContext> options) : base(options)
    {
    }

    public DbSet<Bidding> Biddings { get; set; }
    public DbSet<Lot> Lots { get; set; }
    public DbSet<LotCategory> LotCategories { get; set; }
    public DbSet<LotCadastralNumber> LotCadastralNumbers { get; set; }
    public DbSet<User> Users { get; set; }
    public DbSet<Favorite> Favorites { get; set; }
    public DbSet<LotAuditEvent> LotAuditEvents { get; set; }
    public DbSet<LotClassificationAnalysis> LotClassificationAnalysis { get; set; }
    public DbSet<Subject> Subjects { get; set; }
    public DbSet<LegalCase> LegalCases { get; set; }
    public DbSet<LotImage> LotImages { get; set; }
    public DbSet<LotDocument> Documents { get; set; }
    public DbSet<LotPriceSchedule> LotPriceSchedules { get; set; }
    public DbSet<LotEvaluation> LotEvaluations { get; set; }
    public DbSet<LotEvaluationUserRunStatistics> LotEvaluationUserRunStatistics { get; set; } = default!;


    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasSequence<int>("lots_public_id_seq")
                .StartsAt(10001)
                .IncrementsBy(1);

        modelBuilder.Entity<Lot>(entity =>
        {
            entity.HasOne(c => c.Bidding)
                .WithMany(l => l.Lots)
                .HasForeignKey(c => c.BiddingId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => e.PublicId)
                .IsUnique();

            // Настраиваем автогенерацию значения через Sequence
            entity.Property(e => e.PublicId)
                .ValueGeneratedOnAdd()
                .HasDefaultValueSql("nextval('lots_public_id_seq')");
        });

        modelBuilder.Entity<LotCategory>()
            .HasOne(c => c.Lot)
            .WithMany(l => l.Categories)
            .HasForeignKey(c => c.LotId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<LotCadastralNumber>()
            .HasOne(c => c.Lot)
            .WithMany(l => l.CadastralNumbers)
            .HasForeignKey(c => c.LotId)
            .OnDelete(DeleteBehavior.Cascade);

        // ПОЛНОТЕКСТОВЫЙ ПОИСК
        // Включаем расширения
        modelBuilder.HasPostgresExtension("uuid-ossp");
        modelBuilder.HasPostgresExtension("pg_trgm"); // Для нечеткого поиска (опечатки)
        modelBuilder.HasPostgresExtension("unaccent"); // Чтобы искать "ё" как "е"

        // Индекс для полнотекстового поиска (быстрый поиск по словам)
        // Генерируемый столбец (Generated Column) - лучший способ хранить tsvector в Postgres 12+
        modelBuilder.Entity<Lot>()
        .HasGeneratedTsVectorColumn(
            p => p.SearchVector,
            "russian_h",  // Используем Hunspell Dictionary
            p => new { p.Title, p.Description }
        )
        .HasIndex(p => p.SearchVector)
        .HasMethod("GIN"); // GIN индекс для мгновенного поиска

        // === Оптимизация поиска по кадастровым номерам ===
        // Кадастровые номера часто ищут как "77:01:..." так и "7701..."
        // Создаем индекс по "чистому" номеру (только цифры) для быстрого поиска
        modelBuilder.Entity<LotCadastralNumber>()
        .Property(p => p.CleanCadastralNumber)
        .HasComputedColumnSql(
            "regexp_replace(\"CadastralNumber\", '\\D', '', 'g')",
            stored: true // Важно: сохраняем результат в БД, чтобы можно было построить индекс
        );

        modelBuilder.Entity<LotCadastralNumber>()
        .HasIndex(p => p.CleanCadastralNumber);

        // Добавляем составной индекс для таблицы аудита
        modelBuilder.Entity<LotAuditEvent>()
            .HasIndex(e => new { e.LotId, e.EventType })
            .HasDatabaseName("IX_LotAuditEvents_LotId_EventType");

            // Настройка связи 1 к 1
        modelBuilder.Entity<Bidding>()
            .HasOne(b => b.EnrichmentState)
            .WithOne(s => s.Bidding)
            .HasForeignKey<EnrichmentState>(s => s.BiddingId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

// == run command from Lots.Data project ==
// dotnet ef migrations add InitialCreate --project Lots.Data.csproj --startup-project ../FedresursScraper
// dotnet ef database update --project Lots.Data.csproj --startup-project ../FedresursScraper
// dotnet ef migrations remove --project Lots.Data.csproj --startup-project ../FedresursScraper

// -- удаление БД (Выполните эту команду из корневой папки вашего проекта (B:\Т\FedresursScraper)
// dotnet ef database drop --project Lots.Data --startup-project FedresursScraper