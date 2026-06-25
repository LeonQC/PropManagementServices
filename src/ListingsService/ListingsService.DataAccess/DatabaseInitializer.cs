using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ListingsService.DataAccess;

/// <summary>
/// Applies pending EF Core migrations on startup and seeds the database the first
/// time it's empty. This is the single place that touches the DbContext for
/// provisioning, so the Api layer can trigger it without referencing EF types.
/// </summary>
public static class DatabaseInitializer
{
    public static async Task InitializeAsync(
        IServiceProvider services, string? seedSqlPath, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ListingsDbContext>();
        var logger = scope.ServiceProvider
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("DatabaseInitializer");

        // Apply any migrations this database hasn't seen yet. On an existing
        // (baselined) database this is a no-op; on a fresh one it builds the schema.
        await db.Database.MigrateAsync(ct);

        // Seed only when the database is empty, so we never duplicate data or stomp
        // edits. The seed file is bundled with the app (see Api/Seed).
        if (await db.Properties.AnyAsync(ct))
        {
            logger.LogInformation("Database already has data — skipping seed.");
            return;
        }

        if (string.IsNullOrEmpty(seedSqlPath) || !File.Exists(seedSqlPath))
        {
            logger.LogWarning("Database is empty but seed file not found at {Path} — skipping seed.", seedSqlPath);
            return;
        }

        logger.LogInformation("Empty database — seeding from {Path}.", seedSqlPath);
        var sql = await File.ReadAllTextAsync(seedSqlPath, ct);
        await db.Database.ExecuteSqlRawAsync(sql, ct);
        logger.LogInformation("Seed complete.");
    }
}
