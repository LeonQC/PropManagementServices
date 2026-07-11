using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DealsService.DataAccess;

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
        var db = scope.ServiceProvider.GetRequiredService<DealsDbContext>();
        var logger = scope.ServiceProvider
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("DatabaseInitializer");

        // Retry with backoff: on a fresh volume the Postgres container accepts
        // connections a few seconds after compose starts it (architecture §4.3).
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await db.Database.MigrateAsync(ct);
                break;
            }
            catch (Exception ex) when (attempt < 6)
            {
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
                logger.LogWarning("Database not ready (attempt {Attempt}): {Message}. Retrying in {Delay}s.",
                    attempt, ex.Message, delay.TotalSeconds);
                await Task.Delay(delay, ct);
            }
        }

        // Seed only when the database is empty, so we never duplicate data or stomp
        // edits. The seed file is bundled with the app (see Api/Seed).
        if (await db.Deals.AnyAsync(ct))
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
