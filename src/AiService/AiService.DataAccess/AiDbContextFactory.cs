using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AiService.DataAccess;

/// <summary>
/// Design-time factory used only by the EF Core tools (e.g. `dotnet ef migrations add`).
/// It lets the tooling build an AiDbContext without booting the Api host (and its
/// JWT wiring). Not used at runtime — the app configures the context through
/// AddDataAccess instead.
/// </summary>
public class AiDbContextFactory : IDesignTimeDbContextFactory<AiDbContext>
{
    public AiDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("AI_DB")
            ?? "Host=localhost;Port=5436;Database=proptrack_ai;Username=proptrack;Password=proptrack";

        var options = new DbContextOptionsBuilder<AiDbContext>()
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .Options;

        return new AiDbContext(options);
    }
}
