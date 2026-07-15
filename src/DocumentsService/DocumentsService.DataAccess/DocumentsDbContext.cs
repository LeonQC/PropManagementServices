using DocumentsService.Models;
using Microsoft.EntityFrameworkCore;

namespace DocumentsService.DataAccess;

public class DocumentsDbContext(DbContextOptions<DocumentsDbContext> options) : DbContext(options)
{
    public DbSet<DocumentRecord> DocumentRecords => Set<DocumentRecord>();
    public DbSet<DocumentText> DocumentTexts => Set<DocumentText>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DocumentRecord>(e =>
        {
            e.HasIndex(d => d.Status);
            e.HasIndex(d => d.DealId);
            e.HasIndex(d => d.StorageKey).IsUnique();

            e.HasOne(d => d.Text)
                .WithOne()
                .HasForeignKey<DocumentText>(t => t.DocumentId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DocumentText>(e =>
        {
            // Singular per the architecture doc (§2.4 names the table document_text).
            e.ToTable("document_text");
            e.HasKey(t => t.DocumentId);
        });
    }
}
