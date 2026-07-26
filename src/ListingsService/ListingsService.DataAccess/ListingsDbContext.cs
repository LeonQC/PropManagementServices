using ListingsService.Models;
using Microsoft.EntityFrameworkCore;
using NpgsqlTypes;

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

            // Full-text search: a weighted, GIN-indexed STORED generated tsvector. Weights
            // rank title (A) above description (B) above address (C) in ts_rank. It's a
            // shadow property (no CLR field on the POCO model). Because a generated column
            // can only reference same-row columns, address is fed via the denormalized
            // AddressSearch column (Property.AddressSearch), set at creation.
            e.Property<NpgsqlTsVector>("SearchVector")
                .HasColumnName("search_vector")
                .HasComputedColumnSql(
                    "setweight(to_tsvector('english', coalesce(title, '')), 'A') || " +
                    "setweight(to_tsvector('english', coalesce(description_text, '')), 'B') || " +
                    "setweight(to_tsvector('english', coalesce(address_search, '')), 'C')",
                    stored: true);

            e.HasIndex("SearchVector")
                .HasMethod("gin")
                .HasDatabaseName("ix_properties_search_vector");

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
