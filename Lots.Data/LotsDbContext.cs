using Lots.Data.Entities;
using Lots.Data.Entities.DebtScoring;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

/// <summary>
/// Основной контекст базы данных для работы с лотами и связанными сущностями.
/// </summary>
public class LotsDbContext : DbContext
{
    /// <summary>
    /// Инициализирует новый экземпляр контекста базы данных.
    /// </summary>
    /// <param name="options">Настройки подключения и конфигурации EF Core.</param>
    public LotsDbContext(DbContextOptions<LotsDbContext> options) : base(options)
    {
    }

    public DbSet<Bidding> Biddings { get; set; }
    public DbSet<Lot> Lots { get; set; }
    public DbSet<LotCategory> LotCategories { get; set; }
    public DbSet<LotCadastralNumber> LotCadastralNumbers { get; set; }
    public DbSet<User> Users { get; set; }
    public DbSet<Favorite> Favorites { get; set; }
    public DbSet<LotVote> LotVotes { get; set; }
    public DbSet<UserLotContractPermission> UserLotContractPermissions { get; set; }
    public DbSet<LotAuditEvent> LotAuditEvents { get; set; }
    public DbSet<LotClassificationAnalysis> LotClassificationAnalysis { get; set; }
    public DbSet<Subject> Subjects { get; set; }
    public DbSet<LegalCase> LegalCases { get; set; }
    public DbSet<LotImage> LotImages { get; set; }
    public DbSet<LotDocument> Documents { get; set; }
    public DbSet<LotPriceSchedule> LotPriceSchedules { get; set; }
    public DbSet<LotEvaluation> LotEvaluations { get; set; }
    public DbSet<LotEvaluationUserRunStatistics> LotEvaluationUserRunStatistics { get; set; } = default!;

    public DbSet<RawFedresursMessage> RawFedresursMessages { get; set; }

    /// <summary>
    /// Таблица состояний классификации лотов (используется как очередь для фоновых задач). 
    /// </summary>
    public DbSet<LotClassificationState> LotClassificationStates { get; set; }

    /// <summary>
    /// Настройки поиска лотов под запросы пользователей.
    /// </summary>
    public DbSet<LotAlert> LotAlerts { get; set; }

    /// <summary>
    /// Очередь найденных лотов на отправку пользователю.
    /// </summary>
    public DbSet<LotAlertMatch> LotAlertMatches { get; set; }

    /// <summary>
    /// Кеш похожих лотов
    /// </summary>
    public DbSet<SimilarLot> SimilarLots { get; set; }

    /// <summary>
    /// Результаты торгов
    /// </summary>
    public DbSet<LotTradeResult> LotTradeResults { get; set; }

    public DbSet<BiddingScheduleUpdate> BiddingScheduleUpdates { get; set; }

    /// <summary>
    /// Справочник ссылок Альфалот (номер торгов + лот → URL).
    /// </summary>
    public DbSet<AlfalotLotLink> AlfalotLotLinks { get; set; }

    /// <summary>
    /// Справочник ссылок РАД (идентификатор ЕФРСБ + лот → URL).
    /// </summary>
    public DbSet<RadLotLink> RadLotLinks { get; set; }

    public DbSet<UserAd> UserAds { get; set; }
    public DbSet<UserAdImage> UserAdImages { get; set; }
    public DbSet<UserAdChatRoom> ChatRooms { get; set; }
    public DbSet<UserAdChatMessage> ChatMessages { get; set; }

    public DbSet<DebtLotProfile> DebtLotProfiles { get; set; }
    public DbSet<DebtCourtDocument> DebtCourtDocuments { get; set; }
    public DbSet<DebtExtractedEntity> DebtExtractedEntities { get; set; }
    public DbSet<DebtorEnrichmentProfile> DebtorEnrichmentProfiles { get; set; }
    public DbSet<DebtorFnsSnapshot> DebtorFnsSnapshots { get; set; }
    public DbSet<DebtorBankruptcyCheck> DebtorBankruptcyChecks { get; set; }
    public DbSet<DebtorKadCaseSnapshot> DebtorKadCaseSnapshots { get; set; }
    public DbSet<DebtorFsspRecord> DebtorFsspRecords { get; set; }

    public DbSet<DeepSeekBudgetState> DeepSeekBudgetStates { get; set; }

    public DbSet<DeepSeekCircuitBreaker> DeepSeekCircuitBreakers { get; set; }

    [DbFunction("jsonb_extract_path_text", "pg_catalog")]
    public static string JsonbExtractPathText(Dictionary<string, string> target, string path) => throw new NotSupportedException();

    private static readonly ValueComparer<Dictionary<string, string>?> LotAttributesValueComparer = new(
        (left, right) =>
            ReferenceEquals(left, right) ||
            (left != null && right != null && left.Count == right.Count && left.OrderBy(kvp => kvp.Key).SequenceEqual(right.OrderBy(kvp => kvp.Key))),
        dict => dict == null
            ? 0
            : dict.Aggregate(0, (hash, kvp) => HashCode.Combine(hash, kvp.Key, kvp.Value)),
        dict => dict == null ? null : dict.ToDictionary(kvp => kvp.Key, kvp => kvp.Value));

    /// <summary>
    /// Настраивает модели, связи и индексы при создании контекста.
    /// </summary>
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

            entity.Property(e => e.Attributes)
                .HasColumnType("jsonb")
                .Metadata.SetValueComparer(LotAttributesValueComparer);
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

        modelBuilder.Entity<UserLotContractPermission>()
            .HasOne(p => p.User)
            .WithMany()
            .HasForeignKey(p => p.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<UserLotContractPermission>()
            .HasOne(p => p.Lot)
            .WithMany()
            .HasForeignKey(p => p.LotId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<UserLotContractPermission>()
            .HasIndex(p => new { p.UserId, p.LotId })
            .IsUnique();

        // Настройка объявлений
        modelBuilder.Entity<UserAd>(entity =>
        {
            entity.HasOne(a => a.User)
                .WithMany() // Или .WithMany(u => u.Ads), если есть коллекция в User
                .HasForeignKey(a => a.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // Индекс для быстрого поиска активных объявлений
            entity.HasIndex(a => a.Status);
        });

        modelBuilder.Entity<UserAdImage>(entity =>
        {
            entity.HasOne(i => i.UserAd)
                .WithMany(a => a.Images)
                .HasForeignKey(i => i.UserAdId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Настройка связей для чата
        modelBuilder.Entity<UserAdChatRoom>()
            .HasMany(c => c.Messages)
            .WithOne(m => m.Room)
            .HasForeignKey(m => m.ChatRoomId)
            .OnDelete(DeleteBehavior.Cascade); // При удалении комнаты удаляются сообщения
            
        // Важно для объявлений: при удалении объявления удаляем и чаты по нему
        modelBuilder.Entity<UserAd>()
            .HasMany<UserAdChatRoom>()
            .WithOne(c => c.Ad)
            .HasForeignKey(c => c.AdId)
            .OnDelete(DeleteBehavior.Cascade);

        // ПОЛНОТЕКСТОВЫЙ ПОИСК
        if (Database.ProviderName == "Npgsql.EntityFrameworkCore.PostgreSQL")
        {
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
        }
        else
        {
            modelBuilder.Entity<Lot>().Ignore(p => p.SearchVector);
        }

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

        // --- ДОБАВЛЯЕМ ЧАСТИЧНЫЙ ИНДЕКС ДЛЯ АКТИВНЫХ ЛОТОВ ---
        // Формируем SQL-строку для условия. 
        // В PostgreSQL пустая строка ('') и NULL - это разные вещи.
        // Мы исключаем из индекса все лоты с финальными статусами.
        var finalStatusesSql = string.Join(", ", Lot.FinalTradeStatuses.Select(s => $"'{s}'"));

        modelBuilder.Entity<Lot>()
            .HasIndex(l => l.TradeStatus)
            .HasDatabaseName("IX_Lots_ActiveTradeStatus")
            .HasFilter($"\"TradeStatus\" IS NULL OR \"TradeStatus\" = '' OR \"TradeStatus\" NOT IN ({finalStatusesSql})");

        // --- ИНДЕКС ДЛЯ ПОИСКА ПО КАРТЕ (Bounding Box) ---
        // Создаем композитный индекс по широте и долготе.
        // Делаем его частичным (IS NOT NULL), так как лоты без координат на карте не нужны в принципе.
        modelBuilder.Entity<Lot>()
            .HasIndex(l => new { l.Latitude, l.Longitude })
            .HasDatabaseName("IX_Lots_Coordinates")
            .HasFilter("\"Latitude\" IS NOT NULL AND \"Longitude\" IS NOT NULL");

        modelBuilder.Entity<LotClassificationState>(entity =>
        {
            entity.HasKey(e => e.LotId);

            entity.HasOne(e => e.Lot)
                  .WithOne()
                  .HasForeignKey<LotClassificationState>(e => e.LotId)
                  .OnDelete(DeleteBehavior.Cascade);

            // Частичный индекс только для активных задач очереди (статусы 0=Pending, 1=Processing, 3=Failed)
            entity.HasIndex(e => new { e.Status, e.NextAttemptAt })
                  .HasDatabaseName("IX_ClassificationStates_Queue")
                  .HasFilter("\"Status\" IN (0, 1, 3)");
        });

        modelBuilder.Entity<LotAlert>(entity =>
        {
            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<LotAlertMatch>(entity =>
        {
            entity.HasOne(e => e.LotAlert)
                  .WithMany()
                  .HasForeignKey(e => e.LotAlertId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Lot)
                  .WithMany()
                  .HasForeignKey(e => e.LotId)
                  .OnDelete(DeleteBehavior.Cascade);

            // Индекс для быстрого поиска неотправленных уведомлений (для Delivery Worker)
            entity.HasIndex(e => e.IsSent)
                  .HasDatabaseName("IX_LotAlertMatches_Unsent")
                  .HasFilter("\"IsSent\" = FALSE");
        });

        modelBuilder.Entity<SimilarLot>(entity =>
        {
            // Быстрый поиск похожих лотов для конкретного лота
            entity.HasIndex(e => e.SourceLotId);

            entity.HasOne(e => e.TargetLot)
                .WithMany()
                .HasForeignKey(e => e.TargetLotId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DebtLotProfile>(entity =>
        {
            entity.HasOne(e => e.Lot)
                .WithOne()
                .HasForeignKey<DebtLotProfile>(e => e.LotId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => new { e.Status, e.NextAttemptAt })
                .HasDatabaseName("IX_DebtLotProfiles_Queue")
                .HasFilter("\"Status\" IN (0, 1, 5)");

            entity.HasIndex(e => new { e.Status, e.NextAttemptAt })
                .HasDatabaseName("IX_DebtLotProfiles_EnrichmentQueue")
                .HasFilter("\"Status\" IN (2, 3, 6)");
        });

        modelBuilder.Entity<DebtorEnrichmentProfile>(entity =>
        {
            entity.HasOne(e => e.DebtLotProfile)
                .WithOne(p => p.EnrichmentProfile)
                .HasForeignKey<DebtorEnrichmentProfile>(e => e.LotId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Subject)
                .WithMany()
                .HasForeignKey(e => e.SubjectId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<DebtorFnsSnapshot>(entity =>
        {
            entity.HasOne(e => e.EnrichmentProfile)
                .WithOne(p => p.FnsSnapshot)
                .HasForeignKey<DebtorFnsSnapshot>(e => e.LotId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DebtorBankruptcyCheck>(entity =>
        {
            entity.HasOne(e => e.EnrichmentProfile)
                .WithOne(p => p.BankruptcyCheck)
                .HasForeignKey<DebtorBankruptcyCheck>(e => e.LotId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<LotVote>(entity =>
        {
            entity.HasIndex(e => new { e.LotId, e.UserId })
                  .IsUnique();
        });

        modelBuilder.Entity<DebtorKadCaseSnapshot>(entity =>
        {
            entity.HasOne(e => e.EnrichmentProfile)
                .WithOne(p => p.KadCaseSnapshot)
                .HasForeignKey<DebtorKadCaseSnapshot>(e => e.LotId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DebtorFsspRecord>(entity =>
        {
            entity.HasOne(e => e.EnrichmentProfile)
                .WithMany(p => p.FsspRecords)
                .HasForeignKey(e => e.LotId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => e.LotId)
                .HasDatabaseName("IX_DebtorFsspRecords_LotId");
        });

        modelBuilder.Entity<DebtCourtDocument>(entity =>
        {
            entity.HasOne(e => e.Profile)
                .WithMany(p => p.CourtDocuments)
                .HasForeignKey(e => e.LotId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.LotDocument)
                .WithMany()
                .HasForeignKey(e => e.LotDocumentId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasIndex(e => new { e.LotId, e.SourceUrl })
                .HasDatabaseName("IX_DebtCourtDocuments_LotId_SourceUrl");
        });

        modelBuilder.Entity<DebtExtractedEntity>(entity =>
        {
            entity.HasOne(e => e.Profile)
                .WithMany(p => p.ExtractedEntities)
                .HasForeignKey(e => e.LotId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.CourtDocument)
                .WithMany(d => d.ExtractedEntities)
                .HasForeignKey(e => e.CourtDocumentId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasIndex(e => new { e.LotId, e.EntityType })
                .HasDatabaseName("IX_DebtExtractedEntities_LotId_EntityType");
        });

        modelBuilder.Entity<AlfalotLotLink>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.TradeNumber).HasMaxLength(64).IsRequired();
            entity.Property(e => e.TradeNumberNormalized).HasMaxLength(64).IsRequired();
            entity.Property(e => e.LotNumber).HasMaxLength(64).IsRequired();
            entity.Property(e => e.LotNumberNormalized).HasMaxLength(64).IsRequired();
            entity.Property(e => e.TradeUrl).HasMaxLength(1024).IsRequired();
            entity.Property(e => e.LotUrl).HasMaxLength(1024).IsRequired();
            entity.Property(e => e.Status).HasMaxLength(256);

            entity.HasIndex(e => new { e.TradeNumberNormalized, e.LotNumberNormalized })
                .IsUnique()
                .HasDatabaseName("IX_AlfalotLotLinks_TradeLotNormalized");

            entity.HasIndex(e => e.UpdatedAt)
                .HasDatabaseName("IX_AlfalotLotLinks_UpdatedAt");
        });

        modelBuilder.Entity<RadLotLink>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.EfrsbLotId).HasMaxLength(64).IsRequired();
            entity.Property(e => e.EfrsbLotIdNormalized).HasMaxLength(64).IsRequired();
            entity.Property(e => e.LotNumber).HasMaxLength(64).IsRequired();
            entity.Property(e => e.LotNumberNormalized).HasMaxLength(64).IsRequired();
            entity.Property(e => e.LotCode).HasMaxLength(64);
            entity.Property(e => e.LotUrl).HasMaxLength(1024).IsRequired();
            entity.Property(e => e.Status).HasMaxLength(256);

            entity.HasIndex(e => new { e.EfrsbLotIdNormalized, e.LotNumberNormalized })
                .IsUnique()
                .HasDatabaseName("IX_RadLotLinks_EfrsbLotNormalized");

            entity.HasIndex(e => e.ProductId)
                .IsUnique()
                .HasDatabaseName("IX_RadLotLinks_ProductId");

            entity.HasIndex(e => e.UpdatedAt)
                .HasDatabaseName("IX_RadLotLinks_UpdatedAt");
        });

        modelBuilder.Entity<DeepSeekBudgetState>(entity =>
        {
            entity.HasKey(e => e.PeriodKey);
            entity.Property(e => e.PeriodKey).HasMaxLength(20);
        });

        modelBuilder.Entity<DeepSeekCircuitBreaker>(entity =>
        {
            entity.HasData(new DeepSeekCircuitBreaker
            {
                Id = 1,
                UpdatedAt = new DateTime(2026, 6, 25, 0, 0, 0, DateTimeKind.Utc)
            });
        });

        // Автоматическая настройка всех DateTime свойств для всех сущностей
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            var dateTimeProperties = entityType.GetProperties()
                .Where(p => p.ClrType == typeof(DateTime) || p.ClrType == typeof(DateTime?));

            foreach (var property in dateTimeProperties)
            {
                property.SetValueConverter(new Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTime, DateTime>(
                    v => v.Kind == DateTimeKind.Utc ? v : DateTime.SpecifyKind(v, DateTimeKind.Utc),
                    v => v));
            }
        }
    }
}

// == run command from Lots.Data project ==
// dotnet ef migrations add InitialCreate --project Lots.Data.csproj --startup-project ../FedresursScraper
// dotnet ef database update --project Lots.Data.csproj --startup-project ../FedresursScraper
// dotnet ef migrations remove --project Lots.Data.csproj --startup-project ../FedresursScraper

// -- удаление БД (Выполните эту команду из корневой папки вашего проекта (B:\Т\FedresursScraper)
// dotnet ef database drop --project Lots.Data --startup-project FedresursScraper