using ListingsService.Models;
using Microsoft.EntityFrameworkCore;

namespace ListingsService.DataAccess;

public class ListingsDbContext(DbContextOptions<ListingsDbContext> options) : DbContext(options)
{
    public DbSet<Property> Properties => Set<Property>();
    public DbSet<Address> Addresses => Set<Address>();
    public DbSet<PropertyMedia> PropertyMedia => Set<PropertyMedia>();
    public DbSet<PropertyFeature> PropertyFeatures => Set<PropertyFeature>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Property>(e =>
        {
            // Convention produces "year1noi_estimate"; actual column is "year1_noi_estimate"
            e.Property(p => p.Year1NoiEstimate).HasColumnName("year1_noi_estimate");

            e.HasOne(p => p.Address)
                .WithOne(a => a.Property)
                .HasForeignKey<Address>(a => a.PropertyId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasMany(p => p.Media)
                .WithOne(m => m.Property)
                .HasForeignKey(m => m.PropertyId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasMany(p => p.Features)
                .WithOne(f => f.Property)
                .HasForeignKey(f => f.PropertyId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PropertyFeature>(e =>
        {
            e.HasIndex(f => new { f.PropertyId, f.FeatureName }).IsUnique();
        });
    }
}
