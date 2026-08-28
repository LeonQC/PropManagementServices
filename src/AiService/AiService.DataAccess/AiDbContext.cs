using AiService.Models;
using Microsoft.EntityFrameworkCore;

namespace AiService.DataAccess;

public class AiDbContext(DbContextOptions<AiDbContext> options) : DbContext(options)
{
    public DbSet<PromptTemplate> PromptTemplates => Set<PromptTemplate>();
    public DbSet<AiRequestLog> AiRequestLogs => Set<AiRequestLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PromptTemplate>(e =>
        {
            e.ToTable("prompt_templates");

            // At most one active row per feature. A partial unique index rather than a
            // check in the service: the invariant survives whoever edits the table by
            // hand, which is the entire point of the prompt living in the database.
            e.HasIndex(t => new { t.Feature, t.IsActive })
                .HasDatabaseName("ix_prompt_templates_feature_active")
                .IsUnique()
                .HasFilter("is_active");

            e.HasIndex(t => new { t.Feature, t.Version })
                .HasDatabaseName("ix_prompt_templates_feature_version")
                .IsUnique();
        });

        modelBuilder.Entity<AiRequestLog>(e =>
        {
            e.ToTable("ai_request_log");

            // The cost report reads by feature over a date range; the entity index
            // backs "what has this deal cost so far".
            e.HasIndex(l => new { l.Feature, l.CreatedAt })
                .HasDatabaseName("ix_ai_request_log_feature_created_at");
            e.HasIndex(l => l.EntityId)
                .HasDatabaseName("ix_ai_request_log_entity_id");

            // "What did one assistant question cost, and how many turns did it take"
            // is a group-by over this column; without the index it is a table scan of
            // the whole ledger to find six rows.
            e.HasIndex(l => l.CorrelationId)
                .HasDatabaseName("ix_ai_request_log_correlation_id");
        });

        base.OnModelCreating(modelBuilder);
    }
}
