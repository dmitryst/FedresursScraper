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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Lot>()
            .HasOne(c => c.Bidding)
            .WithMany(l => l.Lots)
            .HasForeignKey(c => c.BiddingId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<LotCategory>()
            .HasOne(c => c.Lot)
            .WithMany(l => l.Categories)
            .HasForeignKey(c => c.LotId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

// == run command from Lots.Data project ==
// dotnet ef migrations add InitialCreate --project Lots.Data.csproj --startup-project ../FedresursScraper
// dotnet ef database update --project Lots.Data.csproj --startup-project ../FedresursScraper

// -- удаление БД (Выполните эту команду из корневой папки вашего проекта (B:\Т\FedresursScraper)
// dotnet ef database drop --project Lots.Data --startup-project FedresursScraper