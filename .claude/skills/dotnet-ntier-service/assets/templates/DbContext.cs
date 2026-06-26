using {{Svc}}Service.Models;
using Microsoft.EntityFrameworkCore;

namespace {{Svc}}Service.DataAccess;

public class {{Svc}}DbContext(DbContextOptions<{{Svc}}DbContext> options) : DbContext(options)
{
    // One DbSet per entity, e.g.:
    // public DbSet<{{Aggregate}}> {{Aggregate}}s => Set<{{Aggregate}}>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // snake_case column/table mapping is automatic via
        // UseSnakeCaseNamingConvention() — only declare what convention can't infer:
        //   - relationships (HasOne/HasMany ... WithOne/WithMany ... HasForeignKey)
        //   - indexes (HasIndex)
        //   - column-name overrides for the digit gotcha, e.g.
        //       e.Property(p => p.Year1NoiEstimate).HasColumnName("year1_noi_estimate");
        //     (convention would produce "year1noi_estimate")
    }
}
