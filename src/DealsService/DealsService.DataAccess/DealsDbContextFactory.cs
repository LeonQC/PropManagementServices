using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DealsService.DataAccess;

/// <summary>
/// Design-time factory used only by the EF Core tools (e.g. `dotnet ef migrations add`).
/// It lets the tooling build a DealsDbContext without booting the Api host (and its
/// Kafka/JWT wiring). Not used at runtime — the app configures the context through
/// AddDataAccess instead.
/// </summary>
public class DealsDbContextFactory : IDesignTimeDbContextFactory<DealsDbContext>
{
    public DealsDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("DEALS_DB")
            ?? "Host=localhost;Port=5434;Database=proptrack_deals;Username=proptrack;Password=proptrack";

        var options = new DbContextOptionsBuilder<DealsDbContext>()
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .Options;

        return new DealsDbContext(options);
    }
}
