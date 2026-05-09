using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Tempo.Api.Data;

/// <summary>
/// Design-time factory for EF Core CLI (migrations) without bootstrapping the web host or JWT config.
/// </summary>
public class TempoDbContextFactory : IDesignTimeDbContextFactory<TempoDbContext>
{
    public TempoDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<TempoDbContext>();
        optionsBuilder.UseNpgsql(
            "Host=127.0.0.1;Database=tempo_ef_design;Username=postgres;Password=postgres");
        return new TempoDbContext(optionsBuilder.Options);
    }
}
