using DealsService.Business.Events;
using DealsService.DataAccess;
using PropTrack.Messaging;

namespace DealsService.Business;

/// <summary>
/// Publishes deal.snapshot. Shared by all four deal services rather than living on
/// DealService, because a task, comment or document write changes what the search index
/// should hold just as much as an edit to the deal itself does — and those services own
/// their own repositories and never touch the deal row.
///
/// Every mutation path must go through here. Anything that skips it doesn't fail loudly;
/// the index just quietly diverges from Postgres until the next republish.
/// </summary>
public class DealSnapshotPublisher(IDealRepository repo, IEventPublisher eventPublisher)
{
    /// <summary>Publishes an already-loaded projection. Used by the backfill, which reads
    /// a whole page at a time.</summary>
    public Task PublishAsync(DealSnapshotRow row, CancellationToken ct = default)
    {
        var d = row.Deal;
        return eventPublisher.PublishAsync(Topics.DealSnapshot, d.Id, new DealSnapshot(
            d.Id, d.Version, d.Name, d.PropertyId, d.PropertyName, d.PropertyType, d.MetroArea,
            d.OccupancyRate, d.MarketCapRateBenchmark,
            d.Stage, d.Priority, d.OwnerId, d.DeadReason,
            d.OfferPrice, d.ProjectedCapRate, d.TargetIrr, d.EquityMultiple, d.ProjectedCloseDate,
            d.AiScore, d.AiScoreRationale, d.RiskFlags,
            d.StageEnteredAt, d.CreatedAt, d.UpdatedAt,
            row.TaskCount, row.DoneTaskCount, row.EarliestOpenTaskDueDate,
            row.StageDwellAverageDays, row.StageDwellSampleCount,
            row.CommentText, row.DocumentText,
            // Deals are never deleted, and terminal deals stay listable — see DealSnapshot.
            Deleted: false), ct);
    }

    /// <summary>
    /// Re-reads the deal and publishes it. For callers that already bumped the version as
    /// part of their own write — the deal services, whose repository calls do it inside the
    /// same SaveChanges. The re-read is what picks up rollups the caller can't see, such as
    /// the tasks a stage transition just templated.
    /// </summary>
    public async Task ReloadAndPublishAsync(string dealId, CancellationToken ct = default)
    {
        var row = await repo.GetForSnapshotAsync(dealId, ct);
        if (row is not null) await PublishAsync(row, ct);
    }

    /// <summary>
    /// Bumps the deal's version, then re-reads and publishes. For the task, comment and
    /// document services: their writes change the deal's projection without touching the
    /// deal row, so without the bump the snapshot would carry a version OpenSearch has
    /// already seen and be rejected as stale.
    /// </summary>
    public async Task BumpReloadAndPublishAsync(string dealId, CancellationToken ct = default)
    {
        if (await repo.BumpVersionAsync(dealId, ct) is null) return;
        await ReloadAndPublishAsync(dealId, ct);
    }
}
