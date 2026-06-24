using Lots.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace FedresursScraper.Tests;

/// <summary>
/// InMemory-провайдер не поддерживает jsonb и postgres computed columns из production-модели.
/// </summary>
internal sealed class TestLotsDbContext : LotsDbContext
{
    public TestLotsDbContext(DbContextOptions<LotsDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Lot>().Ignore(e => e.Attributes);

        modelBuilder.Entity<LotCadastralNumber>()
            .Ignore(e => e.CleanCadastralNumber);
    }
}
