using DealsService.Models;
using Microsoft.EntityFrameworkCore;

namespace DealsService.DataAccess;

public class DealsDbContext(DbContextOptions<DealsDbContext> options) : DbContext(options)
{
    public DbSet<Deal> Deals => Set<Deal>();
    public DbSet<DealStageHistory> DealStageHistory => Set<DealStageHistory>();
    public DbSet<DealTask> DealTasks => Set<DealTask>();
    public DbSet<DealComment> DealComments => Set<DealComment>();
    public DbSet<DealDocument> DealDocuments => Set<DealDocument>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Deal>(e =>
        {
            e.Property(d => d.RiskFlags).HasColumnType("jsonb");

            e.HasIndex(d => d.Stage);
            e.HasIndex(d => d.OwnerId);

            // One live acquisition per property: the service checks before insert,
            // and this partial unique index closes the concurrent-create race.
            // Doubles as the lookup index for property_id queries.
            e.HasIndex(d => d.PropertyId)
                .HasDatabaseName("ix_deals_property_id_active")
                .IsUnique()
                .HasFilter("stage NOT IN ('Acquired', 'Dead')");

            e.HasMany(d => d.History)
                .WithOne()
                .HasForeignKey(h => h.DealId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasMany(d => d.Tasks)
                .WithOne()
                .HasForeignKey(t => t.DealId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasMany(d => d.Comments)
                .WithOne()
                .HasForeignKey(c => c.DealId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasMany(d => d.Documents)
                .WithOne()
                .HasForeignKey(x => x.DealId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DealStageHistory>(e =>
        {
            e.ToTable("deal_stage_history");
            e.HasIndex(h => h.DealId);
        });

        modelBuilder.Entity<DealTask>(e => e.HasIndex(t => t.DealId));
        modelBuilder.Entity<DealComment>(e => e.HasIndex(c => c.DealId));
        modelBuilder.Entity<DealDocument>(e => e.HasIndex(d => d.DealId));
    }
}
