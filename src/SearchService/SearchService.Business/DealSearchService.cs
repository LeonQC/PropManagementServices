using SearchService.Business.DTOs;
using SearchService.DataAccess;
using SearchService.Models;

namespace SearchService.Business;

/// <summary>
/// Serves the deals list from the search index. Thin by design, like
/// <see cref="PropertySearchService"/> — except that the two time-derived values the deals
/// contract exposes, hasOverdueTasks and healthFlags, are computed here against the current
/// clock rather than read off the document.
/// </summary>
public class DealSearchService(IDealIndex index)
{
    public async Task<(List<DealDto> Items, int TotalCount)> SearchAsync(
        int page, int pageSize,
        string? stage, string? ownerId, string? priority, string? propertyType, string? metroArea,
        string? closeDateBefore, string? closeDateAfter,
        double? offerPriceMin, double? offerPriceMax,
        double? capRateMin, double? capRateMax,
        double? occupancyMin, double? occupancyMax,
        bool? hasOverdueTasks, int? staleDays, string? q,
        CancellationToken ct = default)
    {
        var (items, totalCount) = await index.SearchAsync(
            page, pageSize, stage, ownerId, priority, propertyType, metroArea,
            closeDateBefore, closeDateAfter, offerPriceMin, offerPriceMax,
            capRateMin, capRateMax, occupancyMin, occupancyMax,
            hasOverdueTasks, staleDays, q, ct);

        // One timestamp for the whole page, so two deals can't be evaluated either side of
        // midnight — the repository takes the same care with its single `now`.
        var now = DateTime.UtcNow;

        // The deals contract exposes totalCount as an int; the index reports a long.
        return ([.. items.Select(d => MapToDto(d, now))], (int)Math.Min(totalCount, int.MaxValue));
    }

    // ----- index document ↔ business model mapping -----

    private static DealDto MapToDto(DealDocument d, DateTime nowUtc) => new(
        d.EntityId, d.Name, d.PropertyId, d.PropertyName, d.PropertyType, d.MetroArea,
        d.OccupancyRate, d.MarketCapRateBenchmark,
        d.Stage, d.Priority, d.OwnerId, d.DeadReason,
        d.OfferPrice, d.ProjectedCapRate, d.TargetIrr, d.EquityMultiple, d.ProjectedCloseDate,
        d.AiScore, d.AiScoreRationale, d.RiskFlags,
        d.StageEnteredAt, d.CreatedAt, d.UpdatedAt,
        d.TaskCount, d.DoneTaskCount,
        DealHealth.HasOverdueTasks(d, nowUtc),
        DealHealth.Evaluate(d, nowUtc));
}
