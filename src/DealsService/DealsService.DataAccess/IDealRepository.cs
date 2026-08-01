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

    /// <summary>Persists a stage transition atomically: the mutated deal, the
    /// appended history row, and any template tasks for the new stage.</summary>
    Task TransitionAsync(Deal deal, DealStageHistory historyRow, List<DealTask> newTasks,
        CancellationToken ct = default);

    Task<List<DealStageHistory>> GetHistoryAsync(string dealId, CancellationToken ct = default);

    Task<List<StageAggregate>> GetPipelineSummaryAsync(CancellationToken ct = default);
}
