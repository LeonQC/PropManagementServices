using Microsoft.Extensions.Logging;
using SearchService.Business.Events;
using SearchService.DataAccess;
using SearchService.Models;

namespace SearchService.Business;

/// <summary>
/// Turns a deal.snapshot into an indexed document. Twin of <see cref="PropertyIndexingService"/>:
/// the consumer is a thin adapter over this, matching how the other services keep their Kafka
/// consumers as one-liners that delegate to a scoped service.
/// </summary>
public class DealIndexingService(IDealIndex index, ILogger<DealIndexingService> logger)
{
    public async Task ApplySnapshotAsync(DealSnapshot snapshot, CancellationToken ct = default)
    {
        // Deals are never deleted today — terminal (Acquired/Dead) deals stay listable, so they
        // stay indexed. The branch exists because the contract carries the flag, and because a
        // deleted deal must leave the index rather than linger with a marker on it.
        if (snapshot.Deleted)
        {
            await index.DeleteAsync(snapshot.DealId, ct);
            logger.LogInformation("Removed {Id} from the index (deleted).", snapshot.DealId);
            return;
        }

        var indexed = await index.IndexAsync(MapToDocument(snapshot), snapshot.Version, ct);

        if (indexed)
            logger.LogInformation("Indexed {Id} at version {Version}.", snapshot.DealId, snapshot.Version);
    }

    /// <summary>Live document count in the index — used by the health endpoint to compare
    /// against the source row count when verifying a backfill.</summary>
    public Task<long> GetIndexedCountAsync(CancellationToken ct = default) => index.CountAsync(ct);

    // ----- snapshot → index document -----

    private static DealDocument MapToDocument(DealSnapshot s) => new()
    {
        EntityType = "deal",
        EntityId = s.DealId,
        Title = s.Name,
        Body = BuildBody(s),
        Name = s.Name,
        PropertyId = s.PropertyId,
        PropertyName = s.PropertyName,
        PropertyType = s.PropertyType,
        MetroArea = s.MetroArea,
        OccupancyRate = s.OccupancyRate,
        MarketCapRateBenchmark = s.MarketCapRateBenchmark,
        Stage = s.Stage,
        Priority = s.Priority,
        OwnerId = s.OwnerId,
        DeadReason = s.DeadReason,
        OfferPrice = s.OfferPrice,
        ProjectedCapRate = s.ProjectedCapRate,
        TargetIrr = s.TargetIrr,
        EquityMultiple = s.EquityMultiple,
        ProjectedCloseDate = s.ProjectedCloseDate,
        AiScore = s.AiScore,
        AiScoreRationale = s.AiScoreRationale,
        RiskFlags = s.RiskFlags,
        StageEnteredAt = s.StageEnteredAt,
        CreatedAt = s.CreatedAt,
        UpdatedAt = s.UpdatedAt,
        TaskCount = s.TaskCount,
        DoneTaskCount = s.DoneTaskCount,
        EarliestOpenTaskDueDate = s.EarliestOpenTaskDueDate,
        StageDwellAverageDays = s.StageDwellAverageDays,
        StageDwellSampleCount = s.StageDwellSampleCount,
        Version = s.Version,
    };

    /// <summary>
    /// Concatenated searchable text for the shared `body` field, so a cross-entity query can hit
    /// one field without knowing each entity's shape. The comment and document text is the part
    /// Postgres can't match: the deals tsvector covers only name and property name.
    /// </summary>
    private static string BuildBody(DealSnapshot s) => string.Join(' ', new[]
    {
        s.Name,
        s.PropertyName,
        s.PropertyType,
        s.MetroArea,
        s.CommentText,
        s.DocumentText,
    }.Where(v => !string.IsNullOrWhiteSpace(v)));
}
