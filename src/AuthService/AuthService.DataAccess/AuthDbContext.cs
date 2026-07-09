using AuthService.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace AuthService.DataAccess;

/// <summary>
/// EF Core context for the auth schema. Extends IdentityDbContext (Guid keys) and
/// adds the refresh-token and audit-log tables. Identity's own tables are remapped
/// from the default "AspNet*" names to the clean snake_case names the architecture
/// specifies (users, roles, user_roles, ...).
/// </summary>
public class AuthDbContext(DbContextOptions<AuthDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        // Clean table names (UseSnakeCaseNamingConvention handles columns).
        b.Entity<ApplicationUser>(e =>
        {
            e.ToTable("users");
            // Default true so existing rows stay active when this column is added.
            e.Property(u => u.IsActive).HasDefaultValue(true);
        });
        b.Entity<IdentityRole<Guid>>().ToTable("roles");
        b.Entity<IdentityUserRole<Guid>>().ToTable("user_roles");
        b.Entity<IdentityUserClaim<Guid>>().ToTable("user_claims");
        b.Entity<IdentityUserLogin<Guid>>().ToTable("user_logins");
        b.Entity<IdentityUserToken<Guid>>().ToTable("user_tokens");
        b.Entity<IdentityRoleClaim<Guid>>().ToTable("role_claims");

        b.Entity<RefreshToken>(e =>
        {
            e.ToTable("refresh_tokens");
            e.HasKey(t => t.Id);
            e.HasIndex(t => t.TokenHash).IsUnique();
            e.HasIndex(t => t.UserId);
            e.HasOne(t => t.User)
                .WithMany()
                .HasForeignKey(t => t.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<AuditLog>(e =>
        {
            e.ToTable("audit_log");
            e.HasKey(a => a.Id);
            e.HasIndex(a => a.UserId);
            e.HasIndex(a => a.CreatedAt);
        });
    }
}
