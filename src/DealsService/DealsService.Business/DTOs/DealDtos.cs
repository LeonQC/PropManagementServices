namespace DealsService.Business.DTOs;

public record CreateDealDto(
    string PropertyId,
    string PropertyName,
    string? PropertyType,
    string? MetroArea,
    double? OccupancyRate,
    double? MarketCapRateBenchmark,
    string? Name,
    string? Priority,
    double? OfferPrice,
    double? ProjectedCapRate,
    double? TargetIrr,
    double? EquityMultiple,
    string? ProjectedCloseDate);

/// <summary>Partial update — only non-null fields are applied. Stage, DeadReason and
/// OwnerId deliberately absent: transitions go through Advance/Kill, ownership
/// through TransferOwner only.</summary>
public record UpdateDealDto(
    string? Name,
    string? Priority,
    double? OfferPrice,
    double? ProjectedCapRate,
    double? TargetIrr,
    double? EquityMultiple,
    string? ProjectedCloseDate);

/// <summary>The deal list filters. All optional; the repository skips nulls so they
/// compose. Q is free text over the deal's search vector, StaleDays the minimum whole
/// days a deal must have sat in its current stage.</summary>
public record DealFilterDto(
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

/// <summary>A deterministic health signal on a deal (design doc §6.6), computed on
/// read. Severity is "warning" or "critical".</summary>
public record HealthFlagDto(string Type, string Severity, string Message);

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

public record StageHistoryDto(
    string Id,
    string? FromStage,
    string ToStage,
    string ChangedById,
    string ChangedAt,
    int? DaysInStage,
    string? Reason);

public record StageSummaryDto(string Stage, int Count, double TotalValue);

public record PipelineSummaryDto(
    int TotalActiveDeals,
    double TotalPipelineValue,
    IReadOnlyList<StageSummaryDto> Stages);
