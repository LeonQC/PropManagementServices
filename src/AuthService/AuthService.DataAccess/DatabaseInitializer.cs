using AuthService.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AuthService.DataAccess;

/// <summary>
/// Applies pending EF Core migrations on startup, then seeds the role set and a
/// bootstrap admin user the first time the database is empty. Seeding is done
/// through Identity's managers (not raw SQL) so the password is hashed by the
/// configured hasher. This is the single place that touches the DbContext for
/// provisioning, so the Api layer can trigger it without referencing EF types.
/// </summary>
public static class DatabaseInitializer
{
    public static async Task InitializeAsync(
        IServiceProvider services, string adminEmail, string adminPassword, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AuthDbContext>();
        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("DatabaseInitializer");

        await db.Database.MigrateAsync(ct);

        // Ensure every role exists (idempotent).
        var roleManager = sp.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        foreach (var role in Roles.All)
        {
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new IdentityRole<Guid>(role));
        }

        // Seed the bootstrap admin only on an empty user table.
        var userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();
        if (await db.Users.AnyAsync(ct))
        {
            logger.LogInformation("Users already present — skipping admin seed.");
            return;
        }

        var admin = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = adminEmail,
            Email = adminEmail,
            EmailConfirmed = true,
            FullName = "PropTrack Admin",
            CreatedAt = DateTime.UtcNow.ToString("O"),
        };

        var result = await userManager.CreateAsync(admin, adminPassword);
        if (!result.Succeeded)
        {
            logger.LogError("Failed to seed admin user: {Errors}",
                string.Join("; ", result.Errors.Select(e => e.Description)));
            return;
        }

        await userManager.AddToRoleAsync(admin, Roles.Admin);
        logger.LogInformation("Seeded bootstrap admin {Email}.", adminEmail);
    }
}
