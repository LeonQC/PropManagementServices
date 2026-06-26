using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ListingsService.DataAccess;

/// <summary>
/// Design-time factory used only by the EF Core tools (e.g. `dotnet ef migrations add`).
/// It lets the tooling build a ListingsDbContext without booting the Api host (and its
/// Kafka/hosted-service wiring). Not used at runtime — the app configures the context
/// through AddDataAccess instead.
/// </summary>
public class ListingsDbContextFactory : IDesignTimeDbContextFactory<ListingsDbContext>
{
    public ListingsDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("LISTINGS_DB")
            ?? "Host=localhost;Port=5432;Database=proptrack_listings;Username=proptrack;Password=proptrack";

        var options = new DbContextOptionsBuilder<ListingsDbContext>()
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .Options;

        return new ListingsDbContext(options);
    }
}
