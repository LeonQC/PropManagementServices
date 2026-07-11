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
        if (!await db.Users.AnyAsync(ct))
        {
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
            if (result.Succeeded)
            {
                await userManager.AddToRoleAsync(admin, Roles.Admin);
                logger.LogInformation("Seeded bootstrap admin {Email}.", adminEmail);
            }
            else
            {
                logger.LogError("Failed to seed admin user: {Errors}",
                    string.Join("; ", result.Errors.Select(e => e.Description)));
            }
        }
        else
        {
            logger.LogInformation("Users already present — skipping admin seed.");
        }

        await SeedDemoUsersAsync(db, userManager, logger, ct);
    }

    /// <summary>
    /// Demo team members with fixed ids, seeded idempotently (per-id existence
    /// check, so pre-existing databases pick them up on restart). The deals-service
    /// seed data references these ids as deal owners / task assignees / comment
    /// authors — keep the ids in sync with its Seed/seed-data.sql.
    /// </summary>
    private static async Task SeedDemoUsersAsync(
        AuthDbContext db, UserManager<ApplicationUser> userManager, ILogger logger, CancellationToken ct)
    {
        (Guid Id, string Email, string FullName, string Role)[] demoUsers =
        [
            (Guid.Parse("11111111-1111-1111-1111-111111111111"), "ava.chen@proptrack.dev", "Ava Chen", Roles.Analyst),
            (Guid.Parse("22222222-2222-2222-2222-222222222222"), "marcus.webb@proptrack.dev", "Marcus Webb", Roles.Associate),
            (Guid.Parse("33333333-3333-3333-3333-333333333333"), "dana.ortiz@proptrack.dev", "Dana Ortiz", Roles.VP),
            (Guid.Parse("44444444-4444-4444-4444-444444444444"), "sam.patel@proptrack.dev", "Sam Patel", Roles.ManagingDirector),
        ];

        foreach (var (id, email, fullName, role) in demoUsers)
        {
            if (await db.Users.AnyAsync(u => u.Id == id, ct)) continue;

            var user = new ApplicationUser
            {
                Id = id,
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                FullName = fullName,
                CreatedAt = DateTime.UtcNow.ToString("O"),
            };

            var result = await userManager.CreateAsync(user, "Demo1234!");
            if (result.Succeeded)
            {
                await userManager.AddToRoleAsync(user, role);
                logger.LogInformation("Seeded demo user {Email} ({Role}).", email, role);
            }
            else
            {
                logger.LogError("Failed to seed demo user {Email}: {Errors}",
                    email, string.Join("; ", result.Errors.Select(e => e.Description)));
            }
        }
    }
}
