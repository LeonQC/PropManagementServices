using DocumentsService.Models;
using Microsoft.EntityFrameworkCore;

namespace DocumentsService.DataAccess;

public class DocumentsDbContext(DbContextOptions<DocumentsDbContext> options) : DbContext(options)
{
    public DbSet<DocumentRecord> DocumentRecords => Set<DocumentRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DocumentRecord>(e =>
        {
            e.HasIndex(d => d.Status);
            e.HasIndex(d => d.DealId);
            e.HasIndex(d => d.StorageKey).IsUnique();
        });
    }
}
