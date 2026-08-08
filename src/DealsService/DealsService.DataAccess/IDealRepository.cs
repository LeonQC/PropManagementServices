using DealsService.Models;

namespace DealsService.DataAccess;

/// <summary>One deterministic health signal on a deal (design doc §6.6). Severity is
/// "warning" or "critical". Computed on read — never persisted. Deal.RiskFlags is
/// reserved for the later LLM-derived judgment flags.</summary>
public record HealthFlag(string Type, string Severity, string Message);

/// <summary>A deal plus the task rollups the board cards render, and the health
/// flags evaluated for it on this read.</summary>
public record DealWithTaskStats(
    Deal Deal,
    int TaskCount,
    int DoneTaskCount,
    bool HasOverdueTasks,
    IReadOnlyList<HealthFlag> HealthFlags);

/// <summary>Per-stage aggregate for the pipeline summary endpoint.</summary>
public record StageAggregate(string Stage, int Count, double TotalValue);

/// <summary>
/// Everything the deal.snapshot projection carries, in one read. Distinct from
/// <see cref="DealWithTaskStats"/>, which serves the API: this one carries the raw
/// inputs the search index needs rather than the values derived from them.
///
/// <para><paramref name="EarliestOpenTaskDueDate"/> replaces the HasOverdueTasks boolean
/// on purpose. Overdue-ness moves with the clock and no event fires when a task tips
/// over, so indexing the answer would freeze it at write time; indexing the earliest
/// open due date lets the query compare it against today instead.</para>
///
/// <para><paramref name="StageDwellAverageDays"/> / <paramref name="StageDwellSampleCount"/>
/// are the baseline the stale-stage health flag compares against, for this deal's
/// (stage, property type). It is a fleet-wide aggregate that shifts whenever any deal
/// transitions, so the snapshotted copy drifts until the next publish — the trade for
/// being able to re-derive the flag at query time, where it stays fresh as days pass.</para>
/// </summary>
public record DealSnapshotRow(
    Deal Deal,
    int TaskCount,
    int DoneTaskCount,
    string? EarliestOpenTaskDueDate,
    string? CommentText,
    string? DocumentText,
    double? StageDwellAverageDays,
    int StageDwellSampleCount);

/// <summary>Mean historical dwell time in one stage for one property type, plus the
/// number of completed transitions it was averaged over. Feeds the stale-stage flag.</summary>
public record StageDwellAverage(string Stage, string? PropertyType, double AverageDays, int SampleCount);

/// <summary>
/// The deal list query. Every member is optional; nulls are skipped so the filters
/// compose. <paramref name="Q"/> is free text run through the GIN-indexed tsvector,
/// <paramref name="StaleDays"/> the minimum whole days a deal must have sat in its
/// current stage.
/// </summary>
public record DealQuery(
    string? Stage = null,
    string? OwnerId = null,
    string? Priority = null,
    string? PropertyType = null,
    string? MetroArea = null,
    string? CloseDateBefore = null,
    string? CloseDateAfter = null,
    double? OfferPriceMin = null,
    double? OfferPriceMax = null,
    double? CapRateMin = null,
    double? CapRateMax = null,
    bool? HasOverdueTasks = null,
    int? StaleDays = null,
    string? Q = null);

public interface IDealRepository
{
    Task<DealWithTaskStats?> GetByIdAsync(string id, CancellationToken ct = default);

    /// <summary>Cheap existence probe used by sub-resource services for 404s.</summary>
    Task<bool> ExistsAsync(string id, CancellationToken ct = default);

    /// <summary>True when the property already has a deal in a non-terminal stage.
    /// One live acquisition per property at a time — backed by a partial unique
    /// index on deals(property_id) for the concurrent-create race.</summary>
    Task<bool> HasActiveDealForPropertyAsync(string propertyId, CancellationToken ct = default);

    Task<(List<DealWithTaskStats> Items, int TotalCount)> GetAllAsync(
        int page, int pageSize, DealQuery filters, CancellationToken ct = default);

    /// <summary>Creates the deal together with its initial history row and template
    /// tasks in a single SaveChanges, so a deal never exists half-provisioned.</summary>
    Task<Deal> CreateAsync(Deal deal, DealStageHistory initialHistory, List<DealTask> templateTasks,
        CancellationToken ct = default);

    Task UpdateAsync(Deal deal, CancellationToken ct = default);

    /// <summary>Bumps a deal's version without loading it into the caller, for writes that
    /// change its searchable projection without touching the deal row (task, comment,
    /// document). Returns the new version, or null when the deal no longer exists.</summary>
    Task<long?> BumpVersionAsync(string dealId, CancellationToken ct = default);

    /// <summary>The full snapshot projection for one deal, or null when it doesn't exist.</summary>
    Task<DealSnapshotRow?> GetForSnapshotAsync(string dealId, CancellationToken ct = default);

    /// <summary>Every deal with its snapshot projection, for republishing (backfill/reindex).
    /// Deliberately unlike GetAllAsync: no filters at all — terminal deals stay in the index
    /// because the deals list doesn't exclude them either — and ordered by id so paging
    /// can't skip rows.</summary>
    Task<(List<DealSnapshotRow> Items, int TotalCount)> GetAllForReindexAsync(
        int page, int pageSize, CancellationToken ct = default);

    /// <summary>Persists a stage transition atomically: the mutated deal, the
    /// appended history row, and any template tasks for the new stage.</summary>
    Task TransitionAsync(Deal deal, DealStageHistory historyRow, List<DealTask> newTasks,
        CancellationToken ct = default);

    Task<List<DealStageHistory>> GetHistoryAsync(string dealId, CancellationToken ct = default);

    Task<List<StageAggregate>> GetPipelineSummaryAsync(CancellationToken ct = default);
}
