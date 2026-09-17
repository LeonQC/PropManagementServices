using AiService.Models;
using Microsoft.EntityFrameworkCore;

namespace AiService.DataAccess;

public interface IAiWorkFingerprintRepository
{
    /// <summary>What was last recorded for this entity, or null if it has never been handled.</summary>
    Task<AiWorkFingerprint?> GetAsync(string feature, string entityId, CancellationToken ct = default);

    /// <summary>Records that these inputs have been handled.</summary>
    Task UpsertAsync(
        string feature,
        string entityId,
        string inputFingerprint,
        double? lastOutputScore,
        CancellationToken ct = default);
}

public class AiWorkFingerprintRepository(AiDbContext db) : IAiWorkFingerprintRepository
{
    public Task<AiWorkFingerprint?> GetAsync(string feature, string entityId, CancellationToken ct = default) =>
        db.AiWorkFingerprints
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Feature == feature && f.EntityId == entityId, ct);

    /// <summary>
    /// Read-then-write rather than an atomic upsert. Safe because the snapshot topics are
    /// single-partition, so exactly one consumer in the group holds a given deal and no two
    /// handlers ever race on the same row. Partitioning those topics would break that
    /// assumption and this would need ON CONFLICT.
    /// </summary>
    public async Task UpsertAsync(
        string feature,
        string entityId,
        string inputFingerprint,
        double? lastOutputScore,
        CancellationToken ct = default)
    {
        var existing = await db.AiWorkFingerprints
            .FirstOrDefaultAsync(f => f.Feature == feature && f.EntityId == entityId, ct);

        var now = DateTime.UtcNow.ToString("O");

        if (existing is null)
        {
            db.AiWorkFingerprints.Add(new AiWorkFingerprint
            {
                Feature = feature,
                EntityId = entityId,
                InputFingerprint = inputFingerprint,
                LastOutputScore = lastOutputScore,
                ComputedAt = now
            });
        }
        else
        {
            existing.InputFingerprint = inputFingerprint;
            existing.LastOutputScore = lastOutputScore;
            existing.ComputedAt = now;
        }

        await db.SaveChangesAsync(ct);
    }
}
