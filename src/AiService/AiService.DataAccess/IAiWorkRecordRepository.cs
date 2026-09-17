using AiService.Models;
using Microsoft.EntityFrameworkCore;

namespace AiService.DataAccess;

public interface IAiWorkRecordRepository
{
    /// <summary>What was last produced for this entity, or null if nothing has been.</summary>
    Task<AiWorkRecord?> GetAsync(string feature, string entityId, CancellationToken ct = default);

    /// <summary>Records that work has been produced, and the score it describes.</summary>
    Task UpsertAsync(string feature, string entityId, double? lastOutputScore, CancellationToken ct = default);
}

public class AiWorkRecordRepository(AiDbContext db) : IAiWorkRecordRepository
{
    public Task<AiWorkRecord?> GetAsync(string feature, string entityId, CancellationToken ct = default) =>
        db.AiWorkRecords
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Feature == feature && r.EntityId == entityId, ct);

    /// <summary>
    /// Read-then-write rather than an atomic upsert. Safe because deal.snapshot is
    /// single-partition, so exactly one consumer in the group holds a given deal and no two
    /// handlers ever race on the same row. Partitioning that topic would break the assumption
    /// and this would need ON CONFLICT.
    /// </summary>
    public async Task UpsertAsync(
        string feature, string entityId, double? lastOutputScore, CancellationToken ct = default)
    {
        var existing = await db.AiWorkRecords
            .FirstOrDefaultAsync(r => r.Feature == feature && r.EntityId == entityId, ct);

        var now = DateTime.UtcNow.ToString("O");

        if (existing is null)
        {
            db.AiWorkRecords.Add(new AiWorkRecord
            {
                Feature = feature,
                EntityId = entityId,
                LastOutputScore = lastOutputScore,
                ComputedAt = now
            });
        }
        else
        {
            existing.LastOutputScore = lastOutputScore;
            existing.ComputedAt = now;
        }

        await db.SaveChangesAsync(ct);
    }
}
