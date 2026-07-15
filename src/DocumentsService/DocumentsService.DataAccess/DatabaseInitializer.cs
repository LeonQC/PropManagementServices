using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DocumentsService.DataAccess;

/// <summary>
/// Applies pending EF Core migrations on startup. This is the single place that
/// touches the DbContext for provisioning, so the Api layer can trigger it without
/// referencing EF types. No seed step: documents start empty by design.
/// </summary>
public static class DatabaseInitializer
{
    public static async Task InitializeAsync(IServiceProvider services, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DocumentsDbContext>();
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

        logger.LogInformation("Database migrations applied.");
    }
}
