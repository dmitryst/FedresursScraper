using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Lots.Data;

public class LotsDbContextFactory : IDesignTimeDbContextFactory<LotsDbContext>
{
    public LotsDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<LotsDbContext>();
        optionsBuilder.UseNpgsql("Host=localhost;Database=lots_design_time;Username=postgres;Password=postgres");
        return new LotsDbContext(optionsBuilder.Options);
    }
}
