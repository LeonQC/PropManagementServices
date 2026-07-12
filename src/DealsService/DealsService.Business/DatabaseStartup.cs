using DealsService.DataAccess;

namespace DealsService.Business;

/// <summary>
/// Re-exports the data layer's database initialization so the Api can trigger
/// migrate + seed through the Business layer (its only project reference) without
/// taking a direct dependency on DataAccess or EF Core.
/// </summary>
public static class DatabaseStartup
{
    public static Task InitializeDatabaseAsync(
        this IServiceProvider services, string? seedSqlPath, CancellationToken ct = default)
        => DatabaseInitializer.InitializeAsync(services, seedSqlPath, ct);
}
