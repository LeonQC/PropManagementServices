using DealsService.Models;

namespace DealsService.DataAccess;

/// <summary>A deal plus the task rollups the board cards render.</summary>
public record DealWithTaskStats(Deal Deal, int TaskCount, int DoneTaskCount, bool HasOverdueTasks);

/// <summary>Per-stage aggregate for the pipeline summary endpoint.</summary>
public record StageAggregate(string Stage, int Count, double TotalValue);

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
        int page, int pageSize,
        string? stage = null,
        string? ownerId = null,
        string? priority = null,
        CancellationToken ct = default);

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
