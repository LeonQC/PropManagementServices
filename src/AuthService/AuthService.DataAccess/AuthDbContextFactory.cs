using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AuthService.DataAccess;

/// <summary>
/// Design-time factory for the EF Core tools (e.g. `dotnet ef migrations add`).
/// Lets the tooling build an AuthDbContext without booting the Api host. Not used
/// at runtime — the app configures the context through AddDataAccess instead.
/// </summary>
public class AuthDbContextFactory : IDesignTimeDbContextFactory<AuthDbContext>
{
    public AuthDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("AUTH_DB")
            ?? "Host=localhost;Port=5433;Database=proptrack_auth;Username=proptrack;Password=proptrack";

        var options = new DbContextOptionsBuilder<AuthDbContext>()
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .Options;

        return new AuthDbContext(options);
    }
}
