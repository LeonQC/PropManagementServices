namespace SearchService.Business.DTOs;

/// <summary>A deterministic health signal on a deal (design doc §6.6). Severity is "warning"
/// or "critical". Computed per response, never stored — see <see cref="DealHealth"/>.</summary>
public record HealthFlagDto(string Type, string Severity, string Message);

/// <summary>
/// A deal as the search endpoint returns it. Field-for-field identical to
/// DealsService.Business.DTOs.DealDto — the two endpoints answer the same contract, so this
/// must stay in step with it.
/// </summary>
public record DealDto(
    string Id,
    string Name,
    string PropertyId,
    string PropertyName,
    string? PropertyType,
    string? MetroArea,
    double? OccupancyRate,
    double? MarketCapRateBenchmark,
    string Stage,
    string Priority,
    string OwnerId,
    string? DeadReason,
    double? OfferPrice,
    double? ProjectedCapRate,
    double? TargetIrr,
    double? EquityMultiple,
    string? ProjectedCloseDate,
    double? AiScore,
    string? AiScoreRationale,
    string? RiskFlags,
    string StageEnteredAt,
    string CreatedAt,
    string? UpdatedAt,
    int TaskCount,
    int DoneTaskCount,
    bool HasOverdueTasks,
    IReadOnlyList<HealthFlagDto> HealthFlags);
